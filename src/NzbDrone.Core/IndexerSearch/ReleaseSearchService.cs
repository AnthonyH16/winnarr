using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.IndexerSearch
{
    public interface ISearchForReleases
    {
        Task<List<DownloadDecision>> EpisodeSearch(int episodeId, bool userInvokedSearch, bool interactiveSearch);
        Task<List<DownloadDecision>> EpisodeSearch(Episode episode, bool userInvokedSearch, bool interactiveSearch);
        Task<List<DownloadDecision>> SeasonSearch(int seriesId, int seasonNumber, bool missingOnly, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch);
        Task<List<DownloadDecision>> SeasonSearch(int seriesId, int seasonNumber, List<Episode> episodes, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch);
        Task<List<DownloadDecision>> CustomQuerySearch(int episodeId, string query, bool userInvokedSearch, bool interactiveSearch);
        Task<List<DownloadDecision>> CustomQuerySeasonSearch(int seriesId, int seasonNumber, string query, bool userInvokedSearch, bool interactiveSearch);
    }

    public class ReleaseSearchService : ISearchForReleases
    {
        private readonly IIndexerFactory _indexerFactory;
        private readonly ISceneMappingService _sceneMapping;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly IMakeDownloadDecision _makeDownloadDecision;
        private readonly Logger _logger;

        // Static cache for search warnings and info (keyed by episode ID for episode searches, or "s{seriesId}_{seasonNumber}" for season searches)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _searchWarnings = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _searchInfos = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _seasonSearchInfos = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        public ReleaseSearchService(IIndexerFactory indexerFactory,
                                ISceneMappingService sceneMapping,
                                ISeriesService seriesService,
                                IEpisodeService episodeService,
                                IMakeDownloadDecision makeDownloadDecision,
                                Logger logger)
        {
            _indexerFactory = indexerFactory;
            _sceneMapping = sceneMapping;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _makeDownloadDecision = makeDownloadDecision;
            _logger = logger;
        }

        public static string GetSearchWarning(int episodeId)
        {
            _searchWarnings.TryRemove(episodeId, out var warning);
            Console.WriteLine($"[GetSearchWarning] Episode {episodeId} - Returning: '{warning ?? "(null)"}', Thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            return warning;
        }

        public static string GetSearchInfo(int episodeId)
        {
            _searchInfos.TryRemove(episodeId, out var info);
            return info;
        }

        public static string GetSeasonSearchInfo(int seriesId, int seasonNumber)
        {
            var key = $"s{seriesId}_{seasonNumber}";
            _seasonSearchInfos.TryRemove(key, out var info);
            Console.WriteLine($"[GetSeasonSearchInfo] Series {seriesId} Season {seasonNumber} - Returning: '{info ?? "(null)"}'");
            return info;
        }

        public async Task<List<DownloadDecision>> EpisodeSearch(int episodeId, bool userInvokedSearch, bool interactiveSearch)
        {
            var episode = _episodeService.GetEpisode(episodeId);

            return await EpisodeSearch(episode, userInvokedSearch, interactiveSearch);
        }

        public async Task<List<DownloadDecision>> EpisodeSearch(Episode episode, bool userInvokedSearch, bool interactiveSearch)
        {
            var series = _seriesService.GetSeries(episode.SeriesId);

            if (series.SeriesType == SeriesTypes.Daily)
            {
                if (string.IsNullOrWhiteSpace(episode.AirDate))
                {
                    _logger.Error("Daily episode is missing an air date. Try refreshing the series info.");
                    throw new SearchFailedException("Air date is missing");
                }

                return await SearchDaily(series, episode, false, userInvokedSearch, interactiveSearch);
            }

            if (series.SeriesType == SeriesTypes.Anime)
            {
                if (episode.SeasonNumber == 0 &&
                    episode.SceneAbsoluteEpisodeNumber == null &&
                    episode.AbsoluteEpisodeNumber == null)
                {
                    // Search for special episodes in season 0 that don't have absolute episode numbers
                    return await SearchSpecial(series, new List<Episode> { episode }, false, userInvokedSearch, interactiveSearch);
                }

                return await SearchAnime(series, episode, false, userInvokedSearch, interactiveSearch);
            }

            if (series.SeriesType == SeriesTypes.Racing)
            {
                // Racing series use episode title for search (e.g., "MotoGP 2025 Thailand Race")
                return await SearchRacing(series, episode, false, userInvokedSearch, interactiveSearch);
            }

            if (episode.SeasonNumber == 0)
            {
                // Search for special episodes in season 0
                return await SearchSpecial(series, new List<Episode> { episode }, false, userInvokedSearch, interactiveSearch);
            }

            return await SearchSingle(series, episode, false, userInvokedSearch, interactiveSearch);
        }

        public async Task<List<DownloadDecision>> CustomQuerySearch(int episodeId, string query, bool userInvokedSearch, bool interactiveSearch)
        {
            var episode = _episodeService.GetEpisode(episodeId);
            var series = _seriesService.GetSeries(episode.SeriesId);

            _logger.Info("CustomQuerySearch: Searching for episode {0} with custom query: {1}", episodeId, query);

            var searchSpec = Get<SpecialEpisodeSearchCriteria>(series, new List<Episode> { episode }, false, userInvokedSearch, interactiveSearch);

            // Use the custom query as the search term
            searchSpec.EpisodeQueryTitles = new[] { query };

            var downloadDecisions = await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);

            if (downloadDecisions.Any())
            {
                _searchInfos[episode.Id] = $"Custom search results for: {query}";
            }
            else
            {
                _searchWarnings[episode.Id] = $"No results found for custom query: {query}";
            }

            return DeDupeDecisions(downloadDecisions);
        }

        public async Task<List<DownloadDecision>> CustomQuerySeasonSearch(int seriesId, int seasonNumber, string query, bool userInvokedSearch, bool interactiveSearch)
        {
            var series = _seriesService.GetSeries(seriesId);
            var episodes = _episodeService.GetEpisodesBySeason(seriesId, seasonNumber);

            _logger.Info("CustomQuerySeasonSearch: Searching for series {0} season {1} with custom query: {2}", seriesId, seasonNumber, query);

            var searchSpec = Get<SpecialEpisodeSearchCriteria>(series, episodes, false, userInvokedSearch, interactiveSearch);

            // Use the custom query as the search term
            searchSpec.EpisodeQueryTitles = new[] { query };

            var downloadDecisions = await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);

            var key = $"s{seriesId}_{seasonNumber}";
            if (downloadDecisions.Any())
            {
                _seasonSearchInfos[key] = $"Custom search results for: {query}";
            }
            else
            {
                _seasonSearchInfos[key] = $"No results found for custom query: {query}";
            }

            return DeDupeDecisions(downloadDecisions);
        }

        public async Task<List<DownloadDecision>> SeasonSearch(int seriesId, int seasonNumber, bool missingOnly, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            var episodes = _episodeService.GetEpisodesBySeason(seriesId, seasonNumber);

            if (missingOnly)
            {
                episodes = episodes.Where(e => !e.HasFile).ToList();
            }

            return await SeasonSearch(seriesId, seasonNumber, episodes, monitoredOnly, userInvokedSearch, interactiveSearch);
        }

        public async Task<List<DownloadDecision>> SeasonSearch(int seriesId, int seasonNumber, List<Episode> episodes, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            var series = _seriesService.GetSeries(seriesId);

            if (series.SeriesType == SeriesTypes.Anime)
            {
                return await SearchAnimeSeason(series, episodes, monitoredOnly, userInvokedSearch, interactiveSearch);
            }

            if (series.SeriesType == SeriesTypes.Daily)
            {
                return await SearchDailySeason(series, episodes, monitoredOnly, userInvokedSearch, interactiveSearch);
            }

            var mappings = GetSceneSeasonMappings(series, episodes);

            var downloadDecisions = new List<DownloadDecision>();
            var successfulQueries = new List<string>();
            var allAttemptedQueries = new List<string>();

            foreach (var mapping in mappings)
            {
                if (mapping.SeasonNumber == 0)
                {
                    // search for special episodes in season 0
                    downloadDecisions.AddRange(await SearchSpecial(series, mapping.Episodes, monitoredOnly, userInvokedSearch, interactiveSearch));
                    continue;
                }

                if (mapping.Episodes.Count == 1)
                {
                    var searchSpec = Get<SingleEpisodeSearchCriteria>(series, mapping, monitoredOnly, userInvokedSearch, interactiveSearch);
                    searchSpec.SeasonNumber = mapping.SeasonNumber;
                    searchSpec.EpisodeNumber = mapping.EpisodeMapping.EpisodeNumber;

                    // Build accurate query description
                    var queryParts = new List<string>();
                    if (series.TvdbId > 0)
                    {
                        var idPart = $"tvdbid={series.TvdbId}";
                        if (series.TvRageId > 0)
                        {
                            idPart += $" rid={series.TvRageId}";
                        }
                        idPart += $" season={mapping.SeasonNumber} ep={mapping.EpisodeMapping.EpisodeNumber}";
                        queryParts.Add(idPart);
                    }

                    if (mapping.SceneTitles.Any())
                    {
                        foreach (var title in mapping.SceneTitles)
                        {
                            queryParts.Add($"q={title} S{mapping.SeasonNumber:00}E{mapping.EpisodeMapping.EpisodeNumber:00}");
                        }
                    }

                    var queryInfo = string.Join(", ", queryParts);
                    allAttemptedQueries.Add(queryInfo);

                    var decisions = await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);

                    if (decisions.Any())
                    {
                        successfulQueries.Add(queryInfo);
                    }

                    downloadDecisions.AddRange(decisions);
                }
                else
                {
                    var searchSpec = Get<SeasonSearchCriteria>(series, mapping, monitoredOnly, userInvokedSearch, interactiveSearch);
                    searchSpec.SeasonNumber = mapping.SeasonNumber;

                    // Build accurate query description
                    var queryParts = new List<string>();
                    if (series.TvdbId > 0)
                    {
                        var idPart = $"tvdbid={series.TvdbId}";
                        if (series.TvRageId > 0)
                        {
                            idPart += $" rid={series.TvRageId}";
                        }
                        idPart += $" season={mapping.SeasonNumber}";
                        queryParts.Add(idPart);
                    }

                    if (mapping.SceneTitles.Any())
                    {
                        foreach (var title in mapping.SceneTitles)
                        {
                            queryParts.Add($"q={title} Season {mapping.SeasonNumber}");
                        }
                    }

                    var queryInfo = string.Join(", ", queryParts);
                    allAttemptedQueries.Add(queryInfo);

                    var decisions = await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);

                    if (decisions.Any())
                    {
                        successfulQueries.Add(queryInfo);
                    }

                    downloadDecisions.AddRange(decisions);
                }
            }

            var key = $"s{seriesId}_{seasonNumber}";

            if (downloadDecisions.Any() && successfulQueries.Any())
            {
                var infoMessage = $"Results found using: {string.Join(" | ", successfulQueries.Distinct())}";
                _seasonSearchInfos[key] = infoMessage;
                _logger.Info("SeasonSearch: Set info for series {0} season {1}: {2}", seriesId, seasonNumber, infoMessage);
            }
            else if (!downloadDecisions.Any() && allAttemptedQueries.Any())
            {
                // We attempted queries but got no results
                var warningMessage = $"No results found. Searched for: {string.Join(" | ", allAttemptedQueries.Distinct())}";
                _seasonSearchInfos[key] = warningMessage;
                _logger.Info("SeasonSearch: Set warning for series {0} season {1}: {2}", seriesId, seasonNumber, warningMessage);
            }

            return DeDupeDecisions(downloadDecisions);
        }

        private List<SceneSeasonMapping> GetSceneSeasonMappings(Series series, List<Episode> episodes)
        {
            var dict = new Dictionary<SceneSeasonMapping, SceneSeasonMapping>();

            var sceneMappings = _sceneMapping.FindByTvdbId(series.TvdbId);

            // Group the episode by SceneSeasonNumber/SeasonNumber, in 99% of cases this will result in 1 groupedEpisode
            var groupedEpisodes = episodes.ToLookup(v => ((v.SceneSeasonNumber ?? v.SeasonNumber) * 100000) + v.SeasonNumber);

            foreach (var groupedEpisode in groupedEpisodes)
            {
                var episodeMappings = GetSceneEpisodeMappings(series, groupedEpisode.First(), sceneMappings);

                foreach (var episodeMapping in episodeMappings)
                {
                    var seasonMapping = new SceneSeasonMapping
                    {
                        Episodes = groupedEpisode.ToList(),
                        EpisodeMapping = episodeMapping,
                        SceneTitles = episodeMapping.SceneTitles,
                        SearchMode = episodeMapping.SearchMode,
                        SeasonNumber = episodeMapping.SeasonNumber
                    };

                    if (dict.TryGetValue(seasonMapping, out var existing))
                    {
                        existing.Episodes.AddRange(seasonMapping.Episodes);
                        existing.SceneTitles.AddRange(seasonMapping.SceneTitles);
                    }
                    else
                    {
                        dict[seasonMapping] = seasonMapping;
                    }
                }
            }

            foreach (var item in dict)
            {
                item.Value.Episodes = item.Value.Episodes.Distinct().ToList();
                item.Value.SceneTitles = item.Value.SceneTitles.Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();
            }

            return dict.Values.ToList();
        }

        private List<SceneEpisodeMapping> GetSceneEpisodeMappings(Series series, Episode episode)
        {
            var dict = new Dictionary<SceneEpisodeMapping, SceneEpisodeMapping>();

            var sceneMappings = _sceneMapping.FindByTvdbId(series.TvdbId);

            var episodeMappings = GetSceneEpisodeMappings(series, episode, sceneMappings);

            foreach (var episodeMapping in episodeMappings)
            {
                if (dict.TryGetValue(episodeMapping, out var existing))
                {
                    existing.SceneTitles.AddRange(episodeMapping.SceneTitles);
                }
                else
                {
                    dict[episodeMapping] = episodeMapping;
                }
            }

            foreach (var item in dict)
            {
                item.Value.SceneTitles = item.Value.SceneTitles.Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();
            }

            return dict.Values.ToList();
        }

        private IEnumerable<SceneEpisodeMapping> GetSceneEpisodeMappings(Series series, Episode episode, List<SceneMapping> sceneMappings)
        {
            var includeGlobal = true;

            foreach (var sceneMapping in sceneMappings)
            {
                // There are two kinds of mappings:
                // - Mapped on Release Season Number with sceneMapping.SceneSeasonNumber specified and optionally sceneMapping.SeasonNumber. This translates via episode.SceneSeasonNumber/SeasonNumber to specific episodes.
                // - Mapped on Episode Season Number with optionally sceneMapping.SeasonNumber. This translates from episode.SceneSeasonNumber/SeasonNumber to specific releases. (Filter by episode.SeasonNumber or globally)

                var ignoreSceneNumbering = sceneMapping.SceneOrigin == "tvdb" || sceneMapping.SceneOrigin == "unknown:tvdb";
                var mappingSceneSeasonNumber = sceneMapping.SceneSeasonNumber.NonNegative();
                var mappingSeasonNumber = sceneMapping.SeasonNumber.NonNegative();

                // Select scene or tvdb on the episode
                var mappedSeasonNumber = ignoreSceneNumbering ? episode.SeasonNumber : (episode.SceneSeasonNumber ?? episode.SeasonNumber);
                var releaseSeasonNumber = sceneMapping.SceneSeasonNumber.NonNegative() ?? mappedSeasonNumber;

                if (mappingSceneSeasonNumber.HasValue)
                {
                    // Apply the alternative mapping (release to scene/tvdb)
                    var mappedAltSeasonNumber = sceneMapping.SeasonNumber.NonNegative() ?? sceneMapping.SceneSeasonNumber.NonNegative() ?? mappedSeasonNumber;

                    // Check if the mapping applies to the current season
                    if (mappedAltSeasonNumber != mappedSeasonNumber)
                    {
                        continue;
                    }
                }
                else
                {
                    // Check if the mapping applies to the current season
                    if (mappingSeasonNumber.HasValue && mappingSeasonNumber.Value != episode.SeasonNumber)
                    {
                        continue;
                    }
                }

                if (sceneMapping.SearchTerm == series.Title && sceneMapping.FilterRegex.IsNullOrWhiteSpace())
                {
                    // Disable the implied mapping if we have an explicit mapping by the same name
                    includeGlobal = false;
                }

                // By default we do a alt title search in case indexers don't have the release properly indexed.  Services can override this behavior.
                var searchMode = sceneMapping.SearchMode ?? ((mappingSceneSeasonNumber.HasValue && series.CleanTitle != sceneMapping.SearchTerm.CleanSeriesTitle()) ? SearchMode.SearchTitle : SearchMode.Default);

                if (ignoreSceneNumbering)
                {
                    yield return new SceneEpisodeMapping
                    {
                        Episode = episode,
                        SearchMode = searchMode,
                        SceneTitles = new List<string> { sceneMapping.SearchTerm },
                        SeasonNumber = releaseSeasonNumber,
                        EpisodeNumber = episode.EpisodeNumber,
                        AbsoluteEpisodeNumber = episode.AbsoluteEpisodeNumber
                    };
                }
                else
                {
                    yield return new SceneEpisodeMapping
                    {
                        Episode = episode,
                        SearchMode = searchMode,
                        SceneTitles = new List<string> { sceneMapping.SearchTerm },
                        SeasonNumber = releaseSeasonNumber,
                        EpisodeNumber = episode.SceneEpisodeNumber ?? episode.EpisodeNumber,
                        AbsoluteEpisodeNumber = episode.SceneAbsoluteEpisodeNumber ?? episode.AbsoluteEpisodeNumber
                    };
                }
            }

            if (includeGlobal)
            {
                yield return new SceneEpisodeMapping
                {
                    Episode = episode,
                    SearchMode = SearchMode.Default,
                    SceneTitles = new List<string> { series.Title },
                    SeasonNumber = episode.SceneSeasonNumber ?? episode.SeasonNumber,
                    EpisodeNumber = episode.SceneEpisodeNumber ?? episode.EpisodeNumber,
                    AbsoluteEpisodeNumber = episode.SceneSeasonNumber ?? episode.AbsoluteEpisodeNumber
                };
            }
        }

        private async Task<List<DownloadDecision>> SearchSingle(Series series, Episode episode, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            _logger.Info("SearchSingle called for episode {0} (ID: {1})", episode.Title, episode.Id);
            var mappings = GetSceneEpisodeMappings(series, episode);

            var downloadDecisions = new List<DownloadDecision>();
            var successfulQueries = new List<string>();
            var allAttemptedQueries = new List<string>();

            foreach (var mapping in mappings)
            {
                var searchSpec = Get<SingleEpisodeSearchCriteria>(series, mapping, monitoredOnly, userInvokedSearch, interactiveSearch);
                searchSpec.SeasonNumber = mapping.SeasonNumber;
                searchSpec.EpisodeNumber = mapping.EpisodeNumber;

                // Build accurate query description showing ALL parameters
                var queryParts = new List<string>();

                // Add TVDB/TVRage IDs if available
                if (series.TvdbId > 0)
                {
                    var idPart = $"tvdbid={series.TvdbId}";
                    if (series.TvRageId > 0)
                    {
                        idPart += $" rid={series.TvRageId}";
                    }
                    idPart += $" season={mapping.SeasonNumber} ep={mapping.EpisodeNumber}";
                    queryParts.Add(idPart);
                }

                // Add text-based queries
                if (mapping.SceneTitles.Any())
                {
                    foreach (var title in mapping.SceneTitles)
                    {
                        queryParts.Add($"q={title} S{mapping.SeasonNumber:00}E{mapping.EpisodeNumber:00}");
                    }
                }
                else
                {
                    queryParts.Add($"S{mapping.SeasonNumber:00}E{mapping.EpisodeNumber:00}");
                }

                var queryInfo = string.Join(", ", queryParts);
                allAttemptedQueries.Add(queryInfo);

                var decisions = await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);
                _logger.Info("SearchSingle: S{0:00}E{1:00} returned {2} decisions", mapping.SeasonNumber, mapping.EpisodeNumber, decisions.Count);

                if (decisions.Any())
                {
                    successfulQueries.Add(queryInfo);
                    _logger.Info("SearchSingle: Added successful query: {0}", queryInfo);
                }

                downloadDecisions.AddRange(decisions);
            }

            if (downloadDecisions.Any() && successfulQueries.Any())
            {
                var infoMessage = $"Results found using: {string.Join(" | ", successfulQueries.Distinct())}";
                _searchInfos[episode.Id] = infoMessage;
                _logger.Info("SearchSingle: Set info for episode {0}: {1}", episode.Id, infoMessage);
            }
            else if (!downloadDecisions.Any() && allAttemptedQueries.Any())
            {
                var warningMessage = $"No results found. Searched for: {string.Join(" | ", allAttemptedQueries.Distinct())}";
                _searchWarnings[episode.Id] = warningMessage;
                _logger.Info("SearchSingle: Set warning for episode {0}: {1}", episode.Id, warningMessage);
            }
            else
            {
                _logger.Info("SearchSingle: No info/warning set - decisions: {0}, queries: {1}", downloadDecisions.Count, successfulQueries.Count);
            }

            return DeDupeDecisions(downloadDecisions);
        }

        private async Task<List<DownloadDecision>> SearchDaily(Series series, Episode episode, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            var airDate = DateTime.ParseExact(episode.AirDate, Episode.AIR_DATE_FORMAT, CultureInfo.InvariantCulture);
            var searchSpec = Get<DailyEpisodeSearchCriteria>(series, new List<Episode> { episode }, monitoredOnly, userInvokedSearch, interactiveSearch);
            searchSpec.AirDate = airDate;

            var dateFormat = airDate.ToString("yyyy.MM.dd");
            var sceneTitles = searchSpec.CleanSceneTitles.Any() ? $"{string.Join(", ", searchSpec.CleanSceneTitles)} " : "";
            var queryInfo = $"{sceneTitles}{dateFormat}";

            var downloadDecisions = await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);

            if (downloadDecisions.Any())
            {
                _searchInfos[episode.Id] = $"Results found using: {queryInfo}";
            }
            else
            {
                _searchWarnings[episode.Id] = $"No results found. Searched for: {queryInfo}";
            }

            return DeDupeDecisions(downloadDecisions);
        }

        private async Task<List<DownloadDecision>> SearchAnime(Series series, Episode episode, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch, bool isSeasonSearch = false)
        {
            var searchSpec = Get<AnimeEpisodeSearchCriteria>(series, new List<Episode> { episode }, monitoredOnly, userInvokedSearch, interactiveSearch);

            searchSpec.IsSeasonSearch = isSeasonSearch;

            searchSpec.SeasonNumber = episode.SceneSeasonNumber ?? episode.SeasonNumber;
            searchSpec.EpisodeNumber = episode.SceneEpisodeNumber ?? episode.EpisodeNumber;
            searchSpec.AbsoluteEpisodeNumber = episode.SceneAbsoluteEpisodeNumber ?? episode.AbsoluteEpisodeNumber ?? 0;

            var sceneTitles = searchSpec.CleanSceneTitles.Any() ? $"{string.Join(", ", searchSpec.CleanSceneTitles)} " : "";
            var episodeInfo = searchSpec.AbsoluteEpisodeNumber > 0
                ? $"Episode {searchSpec.AbsoluteEpisodeNumber}"
                : $"S{searchSpec.SeasonNumber:00}E{searchSpec.EpisodeNumber:00}";
            var queryInfo = $"{sceneTitles}{episodeInfo}";

            var downloadDecisions = await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);

            if (downloadDecisions.Any())
            {
                _searchInfos[episode.Id] = $"Results found using: {queryInfo}";
            }
            else
            {
                _searchWarnings[episode.Id] = $"No results found. Searched for: {queryInfo}";
            }

            return DeDupeDecisions(downloadDecisions);
        }

        private async Task<List<DownloadDecision>> SearchSpecial(Series series, List<Episode> episodes, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            var downloadDecisions = new List<DownloadDecision>();

            var searchSpec = Get<SpecialEpisodeSearchCriteria>(series, episodes, monitoredOnly, userInvokedSearch, interactiveSearch);

            // build list of queries for each episode in the form: "<series> <episode-title>"
            searchSpec.EpisodeQueryTitles = episodes.Where(e => !string.IsNullOrWhiteSpace(e.Title))
                                                    .Where(e => interactiveSearch || !monitoredOnly || e.Monitored)
                                                    .SelectMany(e => searchSpec.CleanSceneTitles.Select(title => title + " " + SearchCriteriaBase.GetCleanSceneTitle(e.Title)))
                                                    .Distinct(StringComparer.InvariantCultureIgnoreCase)
                                                    .ToArray();

            var titleSearchDecisions = await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);
            downloadDecisions.AddRange(titleSearchDecisions);

            // Set info for episodes if title search found results
            if (titleSearchDecisions.Any() && searchSpec.EpisodeQueryTitles.Any())
            {
                foreach (var episode in episodes)
                {
                    _searchInfos[episode.Id] = $"Results found using: {string.Join(", ", searchSpec.EpisodeQueryTitles.Take(3))}";
                }
            }

            // Search for each episode by season/episode number as well
            foreach (var episode in episodes)
            {
                // Episode needs to be monitored if it's not an interactive search
                if (!interactiveSearch && monitoredOnly && !episode.Monitored)
                {
                    continue;
                }

                downloadDecisions.AddRange(await SearchSingle(series, episode, monitoredOnly, userInvokedSearch, interactiveSearch));
            }

            return DeDupeDecisions(downloadDecisions);
        }

        private async Task<List<DownloadDecision>> SearchRacing(Series series, Episode episode, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            var downloadDecisions = new List<DownloadDecision>();

            var searchSpec = Get<SpecialEpisodeSearchCriteria>(series, new List<Episode> { episode }, monitoredOnly, userInvokedSearch, interactiveSearch);

            // Parse episode title to extract GP name and session type
            var episodeTitle = episode.Title ?? string.Empty;
            var gpName = ExtractRacingGpName(episodeTitle);
            var sessionType = ExtractRacingSessionTypeForSearch(episodeTitle);
            var year = episode.SeasonNumber;

            if (string.IsNullOrWhiteSpace(gpName))
            {
                _logger.Warn("Could not extract GP name from racing episode title: {0}", episodeTitle);
                return downloadDecisions;
            }

            // Use top 2-3 most common scene titles to reduce query count
            var topSceneTitles = searchSpec.CleanSceneTitles.Take(3).ToList();

            // Tier 1: Search with session type (strict - most relevant)
            // Only if we successfully detected a session type
            if (!string.IsNullOrWhiteSpace(sessionType))
            {
                var strictQueries = topSceneTitles
                    .Select(sceneTitle => $"{sceneTitle} {year} {gpName} {sessionType}")
                    .ToArray();

                searchSpec.EpisodeQueryTitles = strictQueries;

                _logger.Info("Racing search Tier 1 (strict) for {0} S{1}E{2} '{3}' using queries: {4}",
                    series.Title, episode.SeasonNumber, episode.EpisodeNumber, episodeTitle,
                    string.Join(", ", searchSpec.EpisodeQueryTitles));

                downloadDecisions.AddRange(await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec));

                // If we got results, return them (don't search broader)
                if (downloadDecisions.Any())
                {
                    _logger.Info("Racing search found {0} results in Tier 1 (strict)", downloadDecisions.Count);
                    _searchInfos[episode.Id] = $"Results found using: {string.Join(", ", strictQueries)}";
                    return DeDupeDecisions(downloadDecisions);
                }

                _logger.Info("Racing search Tier 1 returned no results, trying Tier 2 (broader)");
            }
            else
            {
                // Session type could not be detected - skip Tier 1, go straight to broad search
                _logger.Warn("Could not detect session type from episode title '{0}' - using broad search", episodeTitle);
                _logger.Error("===== SETTING WARNING NOW ===== Episode: {0}, ID: {1}", episodeTitle, episode.Id);

                // Set warning for UI display
                var warning = $"Session type could not be detected from episode title '{episodeTitle}'. Showing all sessions for this event.";
                _searchWarnings[episode.Id] = warning;
                _logger.Info("SEARCH: Set warning for episode {0}: {1}", episode.Id, warning);
            }

            // Tier 2: Search without session type (broader - fallback)
            var broadQueries = topSceneTitles
                .Select(sceneTitle => $"{sceneTitle} {year} {gpName}")
                .ToArray();

            searchSpec.EpisodeQueryTitles = broadQueries;

            var tierLabel = string.IsNullOrWhiteSpace(sessionType) ? "broad (no session detected)" : "broader";
            _logger.Info("Racing search Tier 2 ({0}) for {1} S{2}E{3} '{4}' using queries: {5}",
                tierLabel, series.Title, episode.SeasonNumber, episode.EpisodeNumber, episodeTitle,
                string.Join(", ", searchSpec.EpisodeQueryTitles));

            downloadDecisions.AddRange(await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec));

            if (downloadDecisions.Any())
            {
                _logger.Info("Racing search found {0} results in Tier 2 ({1})", downloadDecisions.Count, tierLabel);
                _searchInfos[episode.Id] = $"Results found using: {string.Join(", ", broadQueries)}";
                return DeDupeDecisions(downloadDecisions);
            }

            _logger.Info("Racing search Tier 2 returned no results, trying Tier 3 (round-based)");

            // Tier 3: Search by round number (for older seasons with inconsistent TVDB naming)
            // Use episode number as round number
            var roundNumber = episode.EpisodeNumber;
            var roundQueries = topSceneTitles
                .SelectMany(sceneTitle => new[]
                {
                    $"{sceneTitle} {year} Round{roundNumber}",      // "MotoGP 2008 Round18"
                    $"{sceneTitle} {year} Round {roundNumber}"      // "MotoGP 2008 Round 18"
                })
                .ToArray();

            searchSpec.EpisodeQueryTitles = roundQueries;

            _logger.Info("Racing search Tier 3 (round-based) for {0} S{1}E{2} '{3}' using queries: {4}",
                series.Title, episode.SeasonNumber, episode.EpisodeNumber, episodeTitle,
                string.Join(", ", searchSpec.EpisodeQueryTitles));

            downloadDecisions.AddRange(await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec));

            if (downloadDecisions.Any())
            {
                _logger.Info("Racing search found {0} results in Tier 3 (round-based)", downloadDecisions.Count);
                _searchInfos[episode.Id] = $"Results found using: {string.Join(", ", roundQueries)}";
            }
            else
            {
                _logger.Warn("Racing search found no results in any tier for: {0}", episodeTitle);
            }

            _searchWarnings.TryGetValue(episode.Id, out var debugWarning);
            _searchInfos.TryGetValue(episode.Id, out var debugInfo);
            Console.WriteLine($"[SearchRacing] Before return - Episode {episode.Id} - Warning: '{debugWarning ?? "(null)"}', Info: '{debugInfo ?? "(null)"}', Thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            return DeDupeDecisions(downloadDecisions);
        }

        private string ExtractRacingGpName(string episodeTitle)
        {
            if (string.IsNullOrWhiteSpace(episodeTitle))
            {
                return null;
            }

            // TVDB formats vary by year/series:
            // 2015-2020: "R1 - Qatar (Free Practice 1)" or "R2 - Spain (Race)"
            // 2020 F1: "Austria (Practice 1)"
            // 2024 F1: "Round 1: Bahrain (Practice 1)"
            // 2024 MotoGP: "Qatar Airways Grand Prix of Qatar MotoGP Free Practice Nr. 1"
            // 2025 MotoGP: "THAILAND - Chang - FP 1"

            // Pattern 1: "Rxx - GPName" or "Round xx: GPName"
            var roundMatch = System.Text.RegularExpressions.Regex.Match(
                episodeTitle,
                @"^R(?:ound)?\s*\d+\s*[-:]\s*([^(]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (roundMatch.Success)
            {
                var gpPart = roundMatch.Groups[1].Value.Trim();
                var parenIndex = gpPart.IndexOf('(');
                if (parenIndex > 0)
                {
                    gpPart = gpPart.Substring(0, parenIndex).Trim();
                }

                return gpPart;
            }

            // Pattern 2: "Grand Prix of GPNAME" (2024 sponsor format)
            var gpOfMatch = System.Text.RegularExpressions.Regex.Match(
                episodeTitle,
                @"Grand\s+Prix\s+of\s+(\w+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (gpOfMatch.Success)
            {
                return gpOfMatch.Groups[1].Value;
            }

            // Pattern 2b: "LOCATION Grand Prix" - F1 2025 format like "Las Vegas Grand Prix 2025"
            var locationGpMatch = System.Text.RegularExpressions.Regex.Match(
                episodeTitle,
                @"([A-Z][a-zA-Z\s]+?)\s+Grand\s+Prix",
                System.Text.RegularExpressions.RegexOptions.None);

            if (locationGpMatch.Success)
            {
                // Extract just the location, removing sponsor names
                var location = locationGpMatch.Groups[1].Value.Trim();
                // Take only the last 1-3 words (the actual location, not sponsors)
                var words = location.Split(' ');
                if (words.Length > 3)
                {
                    location = string.Join(" ", words.Skip(words.Length - 2));
                }
                return location;
            }

            // Pattern 3: "GPName (Session)" - simple format with parens
            var simpleMatch = System.Text.RegularExpressions.Regex.Match(
                episodeTitle,
                @"^([^(]+?)\s*\(",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (simpleMatch.Success)
            {
                return simpleMatch.Groups[1].Value.Trim();
            }

            // Pattern 4: "GPName - Circuit - Session" (dash-separated)
            var parts = episodeTitle.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1)
            {
                return parts[0].Trim();
            }

            return null;
        }

        private string ExtractRacingSessionTypeForSearch(string episodeTitle)
        {
            if (string.IsNullOrWhiteSpace(episodeTitle))
            {
                return null;
            }

            var titleLower = episodeTitle.ToLowerInvariant();

            // Check for specific session types
            if (titleLower.Contains("sprint"))
            {
                return "Sprint";
            }

            // Qualifying formats: "Qualifying", "Quali", "Q 1", "Q 2", "Q1", "Q2"
            if (titleLower.Contains("qualifying") || titleLower.Contains("quali") ||
                System.Text.RegularExpressions.Regex.IsMatch(titleLower, @"\bq\s*[12]\b"))
            {
                return "Qualifying";
            }

            if (titleLower.Contains("superpole race"))
            {
                return "Superpole Race";
            }

            if (titleLower.Contains("superpole"))
            {
                return "Superpole";
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(titleLower, @"\brace\s*[12]\b"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(titleLower, @"\brace\s*([12])\b");
                return $"Race {match.Groups[1].Value}";
            }

            // "Race" but not inside "Free Practice" or similar
            if (System.Text.RegularExpressions.Regex.IsMatch(titleLower, @"\b(?<!free\s)(?<!practice\s)race\b"))
            {
                return "Race";
            }

            // FP patterns: "FP1", "Free Practice 1", "Free Practice Nr. 1", "Practice 1"
            var fpMatch = System.Text.RegularExpressions.Regex.Match(titleLower, @"\b(?:free\s+)?practice\s*(?:nr\.?\s*)?([1-4])\b");
            if (fpMatch.Success)
            {
                return $"FP{fpMatch.Groups[1].Value}";
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(titleLower, @"\bfp[1-4]\b"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(titleLower, @"\b(fp[1-4])\b");
                return match.Groups[1].Value.ToUpperInvariant();
            }

            // For search: Return null if no session detected (don't assume Race!)
            return null;
        }

        private string ExtractRacingSessionType(string episodeTitle)
        {
            if (string.IsNullOrWhiteSpace(episodeTitle))
            {
                return null;
            }

            var titleLower = episodeTitle.ToLowerInvariant();

            // Check for specific session types
            if (titleLower.Contains("sprint"))
            {
                return "Sprint";
            }

            // Qualifying formats: "Qualifying", "Quali", "Q 1", "Q 2", "Q1", "Q2"
            if (titleLower.Contains("qualifying") || titleLower.Contains("quali") ||
                System.Text.RegularExpressions.Regex.IsMatch(titleLower, @"\bq\s*[12]\b"))
            {
                return "Qualifying";
            }

            if (titleLower.Contains("superpole race"))
            {
                return "Superpole Race";
            }

            if (titleLower.Contains("superpole"))
            {
                return "Superpole";
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(titleLower, @"\brace\s*[12]\b"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(titleLower, @"\brace\s*([12])\b");
                return $"Race {match.Groups[1].Value}";
            }

            // "Race" but not inside "Free Practice" or similar
            if (System.Text.RegularExpressions.Regex.IsMatch(titleLower, @"\b(?<!free\s)(?<!practice\s)race\b"))
            {
                return "Race";
            }

            // FP patterns: "FP1", "Free Practice 1", "Free Practice Nr. 1", "Practice 1"
            var fpMatch = System.Text.RegularExpressions.Regex.Match(titleLower, @"\b(?:free\s+)?practice\s*(?:nr\.?\s*)?([1-4])\b");
            if (fpMatch.Success)
            {
                return $"FP{fpMatch.Groups[1].Value}";
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(titleLower, @"\bfp[1-4]\b"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(titleLower, @"\b(fp[1-4])\b");
                return match.Groups[1].Value.ToUpperInvariant();
            }

            // Default to Race if no session type found
            return "Race";
        }

        private async Task<List<DownloadDecision>> SearchAnimeSeason(Series series, List<Episode> episodes, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            var downloadDecisions = new List<DownloadDecision>();

            var searchSpec = Get<AnimeSeasonSearchCriteria>(series, episodes, monitoredOnly, userInvokedSearch, interactiveSearch);

            // Episode needs to be monitored if it's not an interactive search
            // and Ensure episode has an airdate and has already aired
            var episodesToSearch = episodes
                .Where(ep => interactiveSearch || !monitoredOnly || ep.Monitored)
                .Where(ep => ep.AirDateUtc.HasValue && ep.AirDateUtc.Value.Before(DateTime.UtcNow))
                .ToList();

            var seasonsToSearch = GetSceneSeasonMappings(series, episodesToSearch)
                .GroupBy(ep => ep.SeasonNumber)
                .Select(epList => epList.First())
                .ToList();

            foreach (var season in seasonsToSearch)
            {
                searchSpec.SeasonNumber = season.SeasonNumber;

                var decisions = await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec);
                downloadDecisions.AddRange(decisions);
            }

            foreach (var episode in episodesToSearch)
            {
                downloadDecisions.AddRange(await SearchAnime(series, episode, monitoredOnly, userInvokedSearch, interactiveSearch, true));
            }

            return DeDupeDecisions(downloadDecisions);
        }

        private async Task<List<DownloadDecision>> SearchDailySeason(Series series, List<Episode> episodes, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch)
        {
            var downloadDecisions = new List<DownloadDecision>();

            // Episode needs to be monitored if it's not an interactive search
            // and Ensure episode has an airdate
            var episodesToSearch = episodes
                .Where(ep => interactiveSearch || !monitoredOnly || ep.Monitored)
                .Where(ep => ep.AirDate.IsNotNullOrWhiteSpace())
                .ToList();

            foreach (var yearGroup in episodesToSearch.GroupBy(v => DateTime.ParseExact(v.AirDate, Episode.AIR_DATE_FORMAT, CultureInfo.InvariantCulture).Year))
            {
                var yearEpisodes = yearGroup.ToList();

                if (yearEpisodes.Count > 1)
                {
                    var searchSpec = Get<DailySeasonSearchCriteria>(series, yearEpisodes, monitoredOnly, userInvokedSearch, interactiveSearch);
                    searchSpec.Year = yearGroup.Key;

                    downloadDecisions.AddRange(await Dispatch(indexer => indexer.Fetch(searchSpec), searchSpec));
                }
                else
                {
                    downloadDecisions.AddRange(await SearchDaily(series, yearEpisodes.First(), monitoredOnly, userInvokedSearch, interactiveSearch));
                }
            }

            return DeDupeDecisions(downloadDecisions);
        }

        private TSpec Get<TSpec>(Series series, List<Episode> episodes, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch)
            where TSpec : SearchCriteriaBase, new()
        {
            var spec = new TSpec();

            spec.Series = series;
            spec.SceneTitles = _sceneMapping.GetSceneNames(series.TvdbId,
                                                           episodes.Select(e => e.SeasonNumber).Distinct().ToList(),
                                                           episodes.Select(e => e.SceneSeasonNumber ?? e.SeasonNumber).Distinct().ToList());

            spec.Episodes = episodes;
            spec.MonitoredEpisodesOnly = monitoredOnly;
            spec.UserInvokedSearch = userInvokedSearch;
            spec.InteractiveSearch = interactiveSearch;

            if (!spec.SceneTitles.Contains(series.Title, StringComparer.InvariantCultureIgnoreCase))
            {
                spec.SceneTitles.Add(series.Title);
            }

            return spec;
        }

        private TSpec Get<TSpec>(Series series, SceneEpisodeMapping mapping, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch)
            where TSpec : SearchCriteriaBase, new()
        {
            var spec = new TSpec();

            spec.Series = series;
            spec.SceneTitles = mapping.SceneTitles;
            spec.SearchMode = mapping.SearchMode;

            spec.Episodes = new List<Episode> { mapping.Episode };
            spec.MonitoredEpisodesOnly = monitoredOnly;
            spec.UserInvokedSearch = userInvokedSearch;
            spec.InteractiveSearch = interactiveSearch;

            return spec;
        }

        private TSpec Get<TSpec>(Series series, SceneSeasonMapping mapping, bool monitoredOnly, bool userInvokedSearch, bool interactiveSearch)
            where TSpec : SearchCriteriaBase, new()
        {
            var spec = new TSpec();

            spec.Series = series;
            spec.SceneTitles = mapping.SceneTitles;
            spec.SearchMode = mapping.SearchMode;

            spec.Episodes = mapping.Episodes;
            spec.MonitoredEpisodesOnly = monitoredOnly;
            spec.UserInvokedSearch = userInvokedSearch;
            spec.InteractiveSearch = interactiveSearch;

            return spec;
        }

        private async Task<List<DownloadDecision>> Dispatch(Func<IIndexer, Task<IList<ReleaseInfo>>> searchAction, SearchCriteriaBase criteriaBase)
        {
            var indexers = criteriaBase.InteractiveSearch ?
                _indexerFactory.InteractiveSearchEnabled() :
                _indexerFactory.AutomaticSearchEnabled();

            // Filter indexers to untagged indexers and indexers with intersecting tags
            indexers = indexers.Where(i => i.Definition.Tags.Empty() || i.Definition.Tags.Intersect(criteriaBase.Series.Tags).Any()).ToList();

            _logger.ProgressInfo("Searching indexers for {0}. {1} active indexers", criteriaBase, indexers.Count);

            var tasks = indexers.Select(indexer => DispatchIndexer(searchAction, indexer, criteriaBase));

            var batch = await Task.WhenAll(tasks);

            var reports = batch.SelectMany(x => x).ToList();

            _logger.ProgressDebug("Total of {0} reports were found for {1} from {2} indexers", reports.Count, criteriaBase, indexers.Count);

            // Update the last search time for all episodes if at least 1 indexer was searched.
            if (indexers.Any())
            {
                var lastSearchTime = DateTime.UtcNow;
                _logger.Debug("Setting last search time to: {0}", lastSearchTime);

                criteriaBase.Episodes.ForEach(e => e.LastSearchTime = lastSearchTime);
                _episodeService.UpdateLastSearchTime(criteriaBase.Episodes);
            }

            return _makeDownloadDecision.GetSearchDecision(reports, criteriaBase).ToList();
        }

        private async Task<IList<ReleaseInfo>> DispatchIndexer(Func<IIndexer, Task<IList<ReleaseInfo>>> searchAction, IIndexer indexer, SearchCriteriaBase criteriaBase)
        {
            try
            {
                return await searchAction(indexer);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while searching for {0}", criteriaBase);
            }

            return Array.Empty<ReleaseInfo>();
        }

        private List<DownloadDecision> DeDupeDecisions(List<DownloadDecision> decisions)
        {
            // De-dupe reports by guid so duplicate results aren't returned. Pick the one with the least rejections and higher indexer priority.
            return decisions.GroupBy(d => d.RemoteEpisode.Release.Guid)
                .Select(d => d.OrderBy(v => v.Rejections.Count()).ThenBy(v => v.RemoteEpisode?.Release?.IndexerPriority ?? IndexerDefinition.DefaultPriority).First())
                .ToList();
        }
    }
}
