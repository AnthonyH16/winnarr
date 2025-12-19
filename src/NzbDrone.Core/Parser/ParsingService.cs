using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Parser
{
    public interface IParsingService
    {
        Series GetSeries(string title);
        RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, int tvdbId, int tvRageId, string imdbId, SearchCriteriaBase searchCriteria = null);
        RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, Series series);
        RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, int seriesId, IEnumerable<int> episodeIds);
        List<Episode> GetEpisodes(ParsedEpisodeInfo parsedEpisodeInfo, Series series, bool sceneSource, SearchCriteriaBase searchCriteria = null);
        ParsedEpisodeInfo ParseSpecialEpisodeTitle(ParsedEpisodeInfo parsedEpisodeInfo, string releaseTitle, int tvdbId, int tvRageId, string imdbId, SearchCriteriaBase searchCriteria = null);
        ParsedEpisodeInfo ParseSpecialEpisodeTitle(ParsedEpisodeInfo parsedEpisodeInfo, string releaseTitle, Series series);
    }

    public class ParsingService : IParsingService
    {
        private readonly IEpisodeService _episodeService;
        private readonly ISeriesService _seriesService;
        private readonly ISceneMappingService _sceneMappingService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        // Constants for file date disambiguation
        private const int ConfidentMatchDaysDifference = 14; // Days difference to confidently choose one episode
        private const int SuggestMatchDaysWindow = 7;        // Days from air date to suggest match

        public ParsingService(IEpisodeService episodeService,
                              ISeriesService seriesService,
                              ISceneMappingService sceneMappingService,
                              IDiskProvider diskProvider,
                              Logger logger)
        {
            _episodeService = episodeService;
            _seriesService = seriesService;
            _sceneMappingService = sceneMappingService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public Series GetSeries(string title)
        {
            var parsedEpisodeInfo = Parser.ParseTitle(title);

            if (parsedEpisodeInfo == null)
            {
                return _seriesService.FindByTitle(title);
            }

            var tvdbId = _sceneMappingService.FindTvdbId(parsedEpisodeInfo.SeriesTitle, parsedEpisodeInfo.ReleaseTitle, parsedEpisodeInfo.SeasonNumber);

            if (tvdbId.HasValue)
            {
                return _seriesService.FindByTvdbId(tvdbId.Value);
            }

            var series = _seriesService.FindByTitle(parsedEpisodeInfo.SeriesTitle);

            if (series == null && parsedEpisodeInfo.SeriesTitleInfo.AllTitles != null)
            {
                series = GetSeriesByAllTitles(parsedEpisodeInfo);
            }

            if (series == null)
            {
                series = _seriesService.FindByTitle(parsedEpisodeInfo.SeriesTitleInfo.TitleWithoutYear,
                                                    parsedEpisodeInfo.SeriesTitleInfo.Year);
            }

            return series;
        }

        private Series GetSeriesByAllTitles(ParsedEpisodeInfo parsedEpisodeInfo)
        {
            Series foundSeries = null;
            int? foundTvdbId = null;

            // Match each title individually, they must all resolve to the same tvdbid
            foreach (var title in parsedEpisodeInfo.SeriesTitleInfo.AllTitles)
            {
                var series = _seriesService.FindByTitle(title);
                var tvdbId = series?.TvdbId;

                if (series == null)
                {
                    tvdbId = _sceneMappingService.FindTvdbId(title, parsedEpisodeInfo.ReleaseTitle, parsedEpisodeInfo.SeasonNumber);
                }

                if (!tvdbId.HasValue)
                {
                    _logger.Trace("Title {0} not matching any series.", title);
                    continue;
                }

                if (foundTvdbId.HasValue && tvdbId != foundTvdbId)
                {
                    _logger.Trace("Title {0} both matches tvdbid {1} and {2}, no series selected.", parsedEpisodeInfo.SeriesTitle, foundTvdbId, tvdbId);
                    return null;
                }

                if (foundSeries == null)
                {
                    foundSeries = series;
                }

                foundTvdbId = tvdbId;
            }

            if (foundSeries == null && foundTvdbId.HasValue)
            {
                foundSeries = _seriesService.FindByTvdbId(foundTvdbId.Value);
            }

            return foundSeries;
        }

        public RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, int tvdbId, int tvRageId, string imdbId, SearchCriteriaBase searchCriteria = null)
        {
            return Map(parsedEpisodeInfo, tvdbId, tvRageId, imdbId, null, searchCriteria);
        }

        public RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, Series series)
        {
            return Map(parsedEpisodeInfo, 0, 0, null, series, null);
        }

        public RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, int seriesId, IEnumerable<int> episodeIds)
        {
            return new RemoteEpisode
                   {
                       ParsedEpisodeInfo = parsedEpisodeInfo,
                       Series = _seriesService.GetSeries(seriesId),
                       Episodes = _episodeService.GetEpisodes(episodeIds)
                   };
        }

        private RemoteEpisode Map(ParsedEpisodeInfo parsedEpisodeInfo, int tvdbId, int tvRageId, string imdbId, Series series, SearchCriteriaBase searchCriteria)
        {
            var sceneMapping = _sceneMappingService.FindSceneMapping(parsedEpisodeInfo.SeriesTitle, parsedEpisodeInfo.ReleaseTitle, parsedEpisodeInfo.SeasonNumber);

            var remoteEpisode = new RemoteEpisode
            {
                ParsedEpisodeInfo = parsedEpisodeInfo,
                SceneMapping = sceneMapping,
                MappedSeasonNumber = parsedEpisodeInfo.SeasonNumber
            };

            // For now we just detect tvdb vs scene, but we can do multiple 'origins' in the future.
            var sceneSource = true;
            if (sceneMapping != null)
            {
                if (sceneMapping.SeasonNumber.HasValue && sceneMapping.SeasonNumber.Value >= 0 &&
                    sceneMapping.SceneSeasonNumber <= parsedEpisodeInfo.SeasonNumber)
                {
                    remoteEpisode.MappedSeasonNumber += sceneMapping.SeasonNumber.Value - sceneMapping.SceneSeasonNumber.Value;
                }

                if (sceneMapping.SceneOrigin == "tvdb")
                {
                    sceneSource = false;
                }
                else if (sceneMapping.Type == "XemService" &&
                         sceneMapping.SceneSeasonNumber.NonNegative().HasValue &&
                         parsedEpisodeInfo.SeasonNumber == 1 &&
                         sceneMapping.SceneSeasonNumber != parsedEpisodeInfo.SeasonNumber)
                {
                    remoteEpisode.MappedSeasonNumber = sceneMapping.SceneSeasonNumber.Value;
                }
            }

            if (series == null)
            {
                var seriesMatch = FindSeries(parsedEpisodeInfo, tvdbId, tvRageId, imdbId, sceneMapping, searchCriteria);

                if (seriesMatch != null)
                {
                    series = seriesMatch.Series;
                    remoteEpisode.SeriesMatchType = seriesMatch.MatchType;
                }
            }

            if (series != null)
            {
                remoteEpisode.Series = series;

                if (ValidateParsedEpisodeInfo.ValidateForSeriesType(parsedEpisodeInfo, series))
                {
                    remoteEpisode.Episodes = GetEpisodes(parsedEpisodeInfo, series, remoteEpisode.MappedSeasonNumber, sceneSource, searchCriteria);
                }
            }

            remoteEpisode.Languages = parsedEpisodeInfo.Languages;

            if (remoteEpisode.Episodes == null)
            {
                remoteEpisode.Episodes = new List<Episode>();
            }

            if (searchCriteria != null)
            {
                var requestedEpisodes = searchCriteria.Episodes.ToDictionaryIgnoreDuplicates(v => v.Id);
                remoteEpisode.EpisodeRequested = remoteEpisode.Episodes.Any(v => requestedEpisodes.ContainsKey(v.Id));
            }

            return remoteEpisode;
        }

        public List<Episode> GetEpisodes(ParsedEpisodeInfo parsedEpisodeInfo, Series series, bool sceneSource, SearchCriteriaBase searchCriteria = null)
        {
            if (sceneSource)
            {
                var remoteEpisode = Map(parsedEpisodeInfo, 0, 0, null, series, searchCriteria);

                return remoteEpisode.Episodes;
            }

            return GetEpisodes(parsedEpisodeInfo, series, parsedEpisodeInfo.SeasonNumber, sceneSource, searchCriteria);
        }

        private List<Episode> GetEpisodes(ParsedEpisodeInfo parsedEpisodeInfo, Series series, int mappedSeasonNumber, bool sceneSource, SearchCriteriaBase searchCriteria)
        {
            _logger.Info("GETEP-START: Called for series '{0}', season {1}, IsRacingContent={2}, sceneSource={3}",
                series.Title, mappedSeasonNumber, parsedEpisodeInfo.IsRacingContent, sceneSource);

            if (parsedEpisodeInfo.FullSeason)
            {
                if (series.UseSceneNumbering && sceneSource)
                {
                    var episodes = _episodeService.GetEpisodesBySceneSeason(series.Id, mappedSeasonNumber);

                    // If episodes were found by the scene season number return them, otherwise fallback to look-up by season number
                    if (episodes.Any())
                    {
                        return episodes;
                    }
                }

                return _episodeService.GetEpisodesBySeason(series.Id, mappedSeasonNumber);
            }

            // Handle racing content (Formula 1, MotoGP, WSBK)
            if (parsedEpisodeInfo.IsRacingContent)
            {
                _logger.Info("GETEP: IsRacingContent=true, calling FindRacingEpisode for series '{0}', season {1}, GP '{2}', session '{3}'",
                    series.Title, mappedSeasonNumber, parsedEpisodeInfo.RacingGpName, parsedEpisodeInfo.RacingSessionType);

                var racingEpisode = FindRacingEpisode(series, mappedSeasonNumber, parsedEpisodeInfo.RacingGpName, parsedEpisodeInfo.RacingSessionType, parsedEpisodeInfo.RacingClass);

                if (racingEpisode != null)
                {
                    return new List<Episode> { racingEpisode };
                }

                _logger.Warn("GETEP: Unable to find racing episode for GP: {0}, Session: {1}", parsedEpisodeInfo.RacingGpName, parsedEpisodeInfo.RacingSessionType);
                return new List<Episode>();
            }

            if (parsedEpisodeInfo.IsDaily)
            {
                var episodeInfo = GetDailyEpisode(series, parsedEpisodeInfo.AirDate, parsedEpisodeInfo.DailyPart, searchCriteria);

                if (episodeInfo != null)
                {
                    return new List<Episode> { episodeInfo };
                }

                return new List<Episode>();
            }

            if (parsedEpisodeInfo.IsAbsoluteNumbering)
            {
                return GetAnimeEpisodes(series, parsedEpisodeInfo, mappedSeasonNumber, sceneSource, searchCriteria);
            }

            if (parsedEpisodeInfo.IsPossibleSceneSeasonSpecial)
            {
                var parsedSpecialEpisodeInfo = ParseSpecialEpisodeTitle(parsedEpisodeInfo, parsedEpisodeInfo.ReleaseTitle, series);

                if (parsedSpecialEpisodeInfo != null)
                {
                    // Use the season number and disable scene source since the season/episode numbers that were returned are not scene numbers
                    return GetStandardEpisodes(series, parsedSpecialEpisodeInfo, parsedSpecialEpisodeInfo.SeasonNumber, false, searchCriteria);
                }
            }

            if (parsedEpisodeInfo.Special && mappedSeasonNumber != 0)
            {
                return new List<Episode>();
            }

            return GetStandardEpisodes(series, parsedEpisodeInfo, mappedSeasonNumber, sceneSource, searchCriteria);
        }

        public ParsedEpisodeInfo ParseSpecialEpisodeTitle(ParsedEpisodeInfo parsedEpisodeInfo, string releaseTitle, int tvdbId, int tvRageId, string imdbId, SearchCriteriaBase searchCriteria = null)
        {
            if (searchCriteria != null)
            {
                if (tvdbId != 0 && tvdbId == searchCriteria.Series.TvdbId)
                {
                    return ParseSpecialEpisodeTitle(parsedEpisodeInfo, releaseTitle, searchCriteria.Series);
                }

                if (tvRageId != 0 && tvRageId == searchCriteria.Series.TvRageId)
                {
                    return ParseSpecialEpisodeTitle(parsedEpisodeInfo, releaseTitle, searchCriteria.Series);
                }

                if (imdbId.IsNotNullOrWhiteSpace() && imdbId.Equals(searchCriteria.Series.ImdbId, StringComparison.Ordinal))
                {
                    return ParseSpecialEpisodeTitle(parsedEpisodeInfo, releaseTitle, searchCriteria.Series);
                }
            }

            var series = GetSeries(releaseTitle);

            if (series == null)
            {
                series = _seriesService.FindByTitleInexact(releaseTitle);
            }

            if (series == null && tvdbId > 0)
            {
                series = _seriesService.FindByTvdbId(tvdbId);
            }

            if (series == null && tvRageId > 0)
            {
                series = _seriesService.FindByTvRageId(tvRageId);
            }

            if (series == null && imdbId.IsNotNullOrWhiteSpace())
            {
                series = _seriesService.FindByImdbId(imdbId);
            }

            if (series == null)
            {
                _logger.Debug("No matching series {0}", releaseTitle);
                return null;
            }

            return ParseSpecialEpisodeTitle(parsedEpisodeInfo, releaseTitle, series);
        }

        public ParsedEpisodeInfo ParseSpecialEpisodeTitle(ParsedEpisodeInfo parsedEpisodeInfo, string releaseTitle, Series series)
        {
            // SxxE00 episodes are sometimes mapped via TheXEM, don't use episode title parsing in that case.
            if (parsedEpisodeInfo != null && parsedEpisodeInfo.IsPossibleSceneSeasonSpecial && series.UseSceneNumbering)
            {
                if (_episodeService.FindEpisodesBySceneNumbering(series.Id, parsedEpisodeInfo.SeasonNumber, 0).Any())
                {
                    return parsedEpisodeInfo;
                }
            }

            // find special episode in series season 0
            var episode = _episodeService.FindEpisodeByTitle(series.Id, 0, releaseTitle);

            if (episode != null)
            {
                // create parsed info from tv episode
                var info = new ParsedEpisodeInfo
                {
                    ReleaseTitle = releaseTitle,
                    SeriesTitle = series.Title,
                    SeriesTitleInfo = new SeriesTitleInfo
                        {
                            Title = series.Title
                        },
                    SeasonNumber = episode.SeasonNumber,
                    EpisodeNumbers = new int[1] { episode.EpisodeNumber },
                    FullSeason = false,
                    Quality = QualityParser.ParseQuality(releaseTitle),
                    ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(releaseTitle),
                    Languages = LanguageParser.ParseLanguages(releaseTitle),
                    Special = true
                };

                _logger.Debug("Found special episode {0} for title '{1}'", info, releaseTitle);
                return info;
            }

            return null;
        }

        private FindSeriesResult FindSeries(ParsedEpisodeInfo parsedEpisodeInfo, int tvdbId, int tvRageId, string imdbId, SceneMapping sceneMapping, SearchCriteriaBase searchCriteria)
        {
            Series series = null;

            if (sceneMapping != null)
            {
                if (searchCriteria != null && searchCriteria.Series.TvdbId == sceneMapping.TvdbId)
                {
                    return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Alias);
                }

                series = _seriesService.FindByTvdbId(sceneMapping.TvdbId);

                if (series == null)
                {
                    _logger.Debug("No matching series {0}", parsedEpisodeInfo.SeriesTitle);
                    return null;
                }

                return new FindSeriesResult(series, SeriesMatchType.Alias);
            }

            if (searchCriteria != null)
            {
                if (searchCriteria.Series.CleanTitle == parsedEpisodeInfo.SeriesTitle.CleanSeriesTitle())
                {
                    return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Title);
                }

                if (tvdbId > 0 && tvdbId == searchCriteria.Series.TvdbId)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by TVDB ID {0}, an alias may be needed for: {1}", tvdbId, parsedEpisodeInfo.SeriesTitle)
                           .Property("TvdbId", tvdbId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("TvdbIdMatch", tvdbId.ToString(), parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Id);
                }

                if (tvRageId > 0 && tvRageId == searchCriteria.Series.TvRageId && tvdbId <= 0)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by TVRage ID {0}, an alias may be needed for: {1}", tvRageId, parsedEpisodeInfo.SeriesTitle)
                           .Property("TvRageId", tvRageId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("TvRageIdMatch", tvRageId.ToString(), parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Id);
                }

                if (imdbId.IsNotNullOrWhiteSpace() && imdbId.Equals(searchCriteria.Series.ImdbId, StringComparison.Ordinal) && tvdbId <= 0)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by IMDb ID {0}, an alias may be needed for: {1}", imdbId, parsedEpisodeInfo.SeriesTitle)
                           .Property("ImdbId", imdbId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("ImdbIdMatch", imdbId, parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    return new FindSeriesResult(searchCriteria.Series, SeriesMatchType.Id);
                }
            }

            var matchType = SeriesMatchType.Unknown;
            series = _seriesService.FindByTitle(parsedEpisodeInfo.SeriesTitle);

            if (series != null)
            {
                matchType = SeriesMatchType.Title;
            }

            if (series == null && parsedEpisodeInfo.SeriesTitleInfo.AllTitles != null)
            {
                series = GetSeriesByAllTitles(parsedEpisodeInfo);
                matchType = SeriesMatchType.Title;
            }

            if (series == null && parsedEpisodeInfo.SeriesTitleInfo.Year > 0)
            {
                series = _seriesService.FindByTitle(parsedEpisodeInfo.SeriesTitleInfo.TitleWithoutYear, parsedEpisodeInfo.SeriesTitleInfo.Year);
                matchType = SeriesMatchType.Title;
            }

            if (series == null && tvdbId > 0)
            {
                series = _seriesService.FindByTvdbId(tvdbId);

                if (series != null)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by TVDB ID {0}, an alias may be needed for: {1}", tvdbId, parsedEpisodeInfo.SeriesTitle)
                           .Property("TvdbId", tvdbId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("TvdbIdMatch", tvdbId.ToString(), parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    matchType = SeriesMatchType.Id;
                }
            }

            if (series == null && tvRageId > 0 && tvdbId <= 0)
            {
                series = _seriesService.FindByTvRageId(tvRageId);

                if (series != null)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by TVRage ID {0}, an alias may be needed for: {1}", tvRageId, parsedEpisodeInfo.SeriesTitle)
                           .Property("TvRageId", tvRageId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("TvRageIdMatch", tvRageId.ToString(), parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    matchType = SeriesMatchType.Id;
                }
            }

            if (series == null && imdbId.IsNotNullOrWhiteSpace() && tvdbId <= 0)
            {
                series = _seriesService.FindByImdbId(imdbId);

                if (series != null)
                {
                    _logger.ForDebugEvent()
                           .Message("Found matching series by IMDb ID {0}, an alias may be needed for: {1}", imdbId, parsedEpisodeInfo.SeriesTitle)
                           .Property("ImdbId", imdbId)
                           .Property("ParsedEpisodeInfo", parsedEpisodeInfo)
                           .WriteSentryWarn("ImdbIdMatch", imdbId, parsedEpisodeInfo.SeriesTitle)
                           .Log();

                    matchType = SeriesMatchType.Id;
                }
            }

            if (series == null)
            {
                _logger.Debug("No matching series {0}", parsedEpisodeInfo.SeriesTitle);
                return null;
            }

            return new FindSeriesResult(series, matchType);
        }

        private Episode GetDailyEpisode(Series series, string airDate, int? part, SearchCriteriaBase searchCriteria)
        {
            Episode episodeInfo = null;

            if (searchCriteria != null)
            {
                episodeInfo = searchCriteria.Episodes.SingleOrDefault(
                    e => e.AirDate == airDate);
            }

            if (episodeInfo == null)
            {
                episodeInfo = _episodeService.FindEpisode(series.Id, airDate, part);
            }

            return episodeInfo;
        }

        private List<Episode> GetAnimeEpisodes(Series series, ParsedEpisodeInfo parsedEpisodeInfo, int seasonNumber, bool sceneSource, SearchCriteriaBase searchCriteria)
        {
            var result = new List<Episode>();

            var sceneSeasonNumber = _sceneMappingService.GetSceneSeasonNumber(parsedEpisodeInfo.SeriesTitle, parsedEpisodeInfo.ReleaseTitle);

            foreach (var absoluteEpisodeNumber in parsedEpisodeInfo.AbsoluteEpisodeNumbers)
            {
                var episodes = new List<Episode>();

                if (parsedEpisodeInfo.Special)
                {
                    var episode = _episodeService.FindEpisode(series.Id, 0, absoluteEpisodeNumber);
                    episodes.AddIfNotNull(episode);
                }
                else if (sceneSource)
                {
                    // Is there a reason why we excluded season 1 from this handling before?
                    // Might have something to do with the scene name to season number check
                    // If this needs to be reverted tests will need to be added
                    if (sceneSeasonNumber.HasValue)
                    {
                        episodes = _episodeService.FindEpisodesBySceneNumbering(series.Id, sceneSeasonNumber.Value, absoluteEpisodeNumber);

                        if (episodes.Empty())
                        {
                            var episode = _episodeService.FindEpisode(series.Id, sceneSeasonNumber.Value, absoluteEpisodeNumber);
                            episodes.AddIfNotNull(episode);
                        }
                    }
                    else if (parsedEpisodeInfo.SeasonNumber > 1 && parsedEpisodeInfo.EpisodeNumbers.Empty())
                    {
                        episodes = _episodeService.FindEpisodesBySceneNumbering(series.Id, parsedEpisodeInfo.SeasonNumber, absoluteEpisodeNumber);

                        if (episodes.Empty())
                        {
                            var episode = _episodeService.FindEpisode(series.Id, parsedEpisodeInfo.SeasonNumber, absoluteEpisodeNumber);
                            episodes.AddIfNotNull(episode);
                        }
                    }
                    else
                    {
                        episodes = _episodeService.FindEpisodesBySceneNumbering(series.Id, absoluteEpisodeNumber);

                        // Don't allow multiple results without a scene name mapping.
                        if (episodes.Count > 1)
                        {
                            episodes.Clear();
                        }
                    }
                }

                if (episodes.Empty())
                {
                    var episode = _episodeService.FindEpisode(series.Id, absoluteEpisodeNumber);
                    episodes.AddIfNotNull(episode);
                }

                foreach (var episode in episodes)
                {
                    _logger.Debug("Using absolute episode number {0} for: {1} - TVDB: {2}x{3:00}",
                                absoluteEpisodeNumber,
                                series.Title,
                                episode.SeasonNumber,
                                episode.EpisodeNumber);

                    result.Add(episode);
                }
            }

            return result;
        }

        private List<Episode> GetStandardEpisodes(Series series, ParsedEpisodeInfo parsedEpisodeInfo, int mappedSeasonNumber, bool sceneSource, SearchCriteriaBase searchCriteria)
        {
            var result = new List<Episode>();

            if (parsedEpisodeInfo.EpisodeNumbers == null)
            {
                return new List<Episode>();
            }

            foreach (var episodeNumber in parsedEpisodeInfo.EpisodeNumbers)
            {
                if (series.UseSceneNumbering && sceneSource)
                {
                    var episodes = new List<Episode>();

                    if (searchCriteria != null)
                    {
                        episodes = searchCriteria.Episodes.Where(e => e.SceneSeasonNumber == parsedEpisodeInfo.SeasonNumber &&
                                                                      e.SceneEpisodeNumber == episodeNumber).ToList();
                    }

                    if (!episodes.Any())
                    {
                        episodes = _episodeService.FindEpisodesBySceneNumbering(series.Id, mappedSeasonNumber, episodeNumber);
                    }

                    if (episodes != null && episodes.Any())
                    {
                        _logger.Debug("Using Scene to TVDB Mapping for: {0} - Scene: {1}x{2:00} - TVDB: {3}",
                                    series.Title,
                                    episodes.First().SceneSeasonNumber,
                                    episodes.First().SceneEpisodeNumber,
                                    string.Join(", ", episodes.Select(e => string.Format("{0}x{1:00}", e.SeasonNumber, e.EpisodeNumber))));

                        result.AddRange(episodes);
                        continue;
                    }
                }

                Episode episodeInfo = null;

                if (searchCriteria != null)
                {
                    episodeInfo = searchCriteria.Episodes.SingleOrDefault(e => e.SeasonNumber == mappedSeasonNumber && e.EpisodeNumber == episodeNumber);
                }

                if (episodeInfo == null)
                {
                    episodeInfo = _episodeService.FindEpisode(series.Id, mappedSeasonNumber, episodeNumber);
                }

                if (episodeInfo != null)
                {
                    result.Add(episodeInfo);
                }
                else
                {
                    _logger.Debug("Unable to find {0}", parsedEpisodeInfo);
                }
            }

            return result;
        }

        // GP name mappings: key = normalized name from filename, values = variations to search in TVDB titles
        // For countries with multiple circuits, use circuit-specific entries to prevent wrong matches
        private static readonly Dictionary<string, string[]> GpNameVariations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "argentina", new[] { "arg", "argentina", "termas" } },
            { "australia", new[] { "aus", "australia", "australian", "phillip island" } },
            { "austria", new[] { "aut", "austria", "austrian", "österreich", "osterreich", "red bull ring", "spielberg" } },
            { "österreich", new[] { "österreich", "osterreich", "austria", "austrian", "red bull ring", "spielberg" } },
            { "osterreich", new[] { "osterreich", "österreich", "austria", "austrian", "red bull ring", "spielberg" } },
            { "spielberg", new[] { "spielberg", "aut", "austria", "austrian", "österreich", "osterreich", "red bull ring" } },
            { "styria", new[] { "styria", "styrian", "steiermark" } },
            { "styrian", new[] { "styrian", "styria", "steiermark" } },
            { "steiermark", new[] { "steiermark", "styria", "styrian" } },
            { "azerbaijan", new[] { "aze", "azerbaijan", "baku" } },
            { "bahrain", new[] { "bhr", "bahrain", "sakhir" } },
            { "belgium", new[] { "bel", "belgium", "belgian", "spa" } },
            { "brazil", new[] { "bra", "brazil", "brazilian", "interlagos", "sao paulo", "são paulo" } },
            { "britain", new[] { "gbr", "britain", "british", "silverstone", "united kingdom", "uk", "great britain" } },
            { "great britain", new[] { "gbr", "britain", "british", "silverstone", "united kingdom", "uk", "great britain" } },
            { "united kingdom", new[] { "gbr", "britain", "british", "silverstone", "uk", "great britain" } },
            { "canada", new[] { "can", "canada", "canadian", "montreal" } },
            { "china", new[] { "chn", "china", "chinese", "shanghai" } },
            { "czech", new[] { "cze", "czech", "brno" } },
            { "czech republic", new[] { "cze", "czech", "brno" } },
            { "france", new[] { "fra", "france", "le mans", "paul ricard" } },
            { "hungary", new[] { "hun", "hungary", "hungarian", "hungaroring", "budapest" } },
            { "india", new[] { "ind", "india", "buddh" } },
            { "indonesia", new[] { "ina", "indonesia", "mandalika", "lombok" } },
            { "kazakhstan", new[] { "kaz", "kazakhstan" } },
            { "malaysia", new[] { "mal", "malaysia", "sepang" } },
            { "mexico", new[] { "mex", "mexico", "mexican" } },
            { "monaco", new[] { "mon", "monaco", "monte carlo" } },
            { "netherlands", new[] { "ned", "netherlands", "assen", "dutch" } },
            { "qatar", new[] { "qat", "qatar", "losail", "lusail" } },
            { "russia", new[] { "rus", "russia", "russian", "sochi" } },
            { "russian", new[] { "russian", "russia", "rus", "sochi" } },
            { "singapore", new[] { "sgp", "singapore", "marina bay" } },
            { "thailand", new[] { "tha", "thailand", "buriram", "chang" } },
            { "turkey", new[] { "tur", "turkey", "turkish", "istanbul" } },
            { "turkish", new[] { "turkish", "turkey", "tur", "istanbul" } },
            { "vietnam", new[] { "vie", "vietnam", "hanoi" } },

            // USA circuits - split by specific circuit to prevent COTA matching to Miami
            { "cota", new[] { "cota", "austin", "united states", "usa", "americas" } },
            { "austin", new[] { "austin", "cota", "united states", "usa", "americas" } },
            { "miami", new[] { "miami" } },
            { "las vegas", new[] { "las vegas", "vegas" } },
            { "vegas", new[] { "vegas", "las vegas" } },
            { "indianapolis", new[] { "indianapolis", "indy" } },
            { "usa", new[] { "united states", "usa", "americas" } }, // Generic USA (no specific circuit)
            { "united states", new[] { "united states", "usa", "americas" } },
            { "americas", new[] { "americas", "united states", "usa" } },

            // Italy circuits - split by specific circuit
            { "monza", new[] { "monza", "italia", "italian", "italy" } },
            { "mugello", new[] { "mugello", "toscana", "tuscany" } },
            { "misano", new[] { "misano" } }, // Note: shared between Italy and San Marino GPs
            { "imola", new[] { "imola", "emilia", "emilia romagna" } },
            { "emilia", new[] { "emilia", "imola", "emilia romagna" } },
            { "emilia romagna", new[] { "emilia romagna", "emilia", "imola" } },
            { "toscana", new[] { "toscana", "tuscany", "mugello" } },
            { "tuscany", new[] { "tuscany", "toscana", "mugello" } },
            { "italy", new[] { "italy", "ita", "italia", "italian" } }, // Generic Italy (no specific circuit)

            // Spain circuits - split by specific circuit
            // NOTE: DO NOT include generic "spain"/"spanish" in specific circuits - causes ambiguous matches
            // e.g., "Spain Aragon" was matching both Jerez "Spanish Grand Prix" AND Aragon GP
            { "catalunya", new[] { "catalunya", "catalonia", "barcelona", "catalan" } },
            { "catalonia", new[] { "catalonia", "catalunya", "barcelona", "catalan" } },
            { "barcelona", new[] { "barcelona", "catalunya", "catalonia", "catalan" } },
            { "jerez", new[] { "jerez", "angel nieto", "circuito de jerez" } },
            { "valencia", new[] { "valencia", "ricardo tormo", "comunitat valenciana", "valenciana" } },
            { "aragon", new[] { "aragon", "aragón", "motorland", "teruel" } },
            // Generic "Spain"/"Spanish" without a specific circuit = Jerez (the "Spanish Grand Prix")
            { "spain", new[] { "spain", "españa", "espana", "esp", "spanish", "jerez" } },
            { "spanish", new[] { "spanish", "spain", "españa", "espana", "jerez" } },

            // Japan circuits - split by specific circuit
            { "suzuka", new[] { "suzuka", "japan" } },
            { "motegi", new[] { "motegi", "japan" } },
            { "japan", new[] { "japan", "japanese", "jpn" } }, // Generic Japan (no specific circuit)

            // Germany circuits - split by specific circuit
            { "hockenheim", new[] { "hockenheim", "germany" } },
            { "nurburgring", new[] { "nurburgring", "eifel", "germany" } },
            { "sachsenring", new[] { "sachsenring", "germany" } },
            { "eifel", new[] { "eifel", "nurburgring", "germany" } },
            { "germany", new[] { "germany", "ger", "deutschland", "eifel", "german" } }, // Generic Germany (no specific circuit)
            { "deutschland", new[] { "deutschland", "germany", "ger", "german" } },
            { "german", new[] { "german", "germany", "deutschland", "ger" } },

            // Portugal circuits - split by specific circuit
            { "portimao", new[] { "portimao", "algarve", "portugal" } },
            { "algarve", new[] { "algarve", "portimao", "portugal" } },
            { "estoril", new[] { "estoril", "portugal" } },
            { "portugal", new[] { "portugal", "portuguese", "por" } }, // Generic Portugal (no specific circuit)

            // Europe GP (held at various circuits, historically Valencia)
            { "europa", new[] { "europa", "europe", "european", "valencia" } },
            { "europe", new[] { "europe", "europa", "european", "valencia" } },
            { "european", new[] { "european", "europe", "europa", "valencia" } },

            // San Marino uses Misano circuit
            { "san marino", new[] { "san marino", "misano", "rimini" } },

            // Saudi Arabia
            { "saudi", new[] { "saudi", "saudi arabia", "jeddah" } },
            { "saudi arabia", new[] { "saudi arabia", "saudi", "jeddah" } },
            { "jeddah", new[] { "jeddah", "saudi", "saudi arabia" } },
            { "arabia", new[] { "saudi arabia", "saudi", "jeddah" } },
            { "saudita", new[] { "saudi arabia", "saudi", "jeddah" } }, // Spanish naming

            // Common nationality adjectives
            { "hungarian", new[] { "hun", "hungary", "hungaroring" } },
            { "british", new[] { "gbr", "britain", "british", "silverstone", "great britain", "uk" } },
            { "american", new[] { "americas", "united states", "usa" } },
            { "italian", new[] { "ita", "italy" } },
            { "french", new[] { "fra", "france" } },
            { "japanese", new[] { "jpn", "japan" } },
            { "dutch", new[] { "ned", "netherlands", "assen" } },
            { "portuguese", new[] { "por", "portugal" } },
            { "mexican", new[] { "mex", "mexico" } },
            { "canadian", new[] { "can", "canada", "canadian", "montreal" } },
            { "chinese", new[] { "chn", "china", "chinese", "shanghai" } },
            { "belgian", new[] { "bel", "belgium", "belgian", "spa" } },
            { "australian", new[] { "aus", "australia", "australian", "phillip island" } },
            { "austrian", new[] { "aut", "austria", "austrian", "österreich", "osterreich", "red bull ring", "spielberg" } },
            { "brazilian", new[] { "bra", "brazil", "brazilian", "interlagos", "sao paulo", "são paulo" } },
        };

        /// <summary>
        /// Known double-header circuits where the same circuit hosted multiple GPs in the same season.
        /// Maps circuit name to the specific GP names that can disambiguate.
        /// </summary>
        private static readonly Dictionary<string, string[]> DoubleHeaderDisambiguators = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // 2020 COVID double-headers
            { "jerez", new[] { "andalucia", "andalusia" } },           // Spain vs Andalucia GP
            { "red bull ring", new[] { "styria", "styrian", "steiermark" } }, // Austria vs Styria GP
            { "spielberg", new[] { "styria", "styrian", "steiermark" } },
            { "misano", new[] { "emilia", "romagna" } },               // San Marino vs Emilia Romagna GP
            { "aragon", new[] { "teruel" } },                          // Aragon vs Teruel GP
            { "valencia", new[] { "europe", "europa", "european" } },  // Valencia vs Europe GP
            { "silverstone", new[] { "70th", "anniversary" } },        // Britain vs 70th Anniversary GP
            { "mugello", new[] { "toscana", "tuscany", "tuscan" } },   // Italy vs Toscana GP
            { "bahrain", new[] { "sakhir" } },                         // Bahrain vs Sakhir GP
            // 2021 double-headers
            { "losail", new[] { "doha" } },                            // Qatar vs Doha GP
            { "lusail", new[] { "doha" } },
            { "portimao", new[] { "algarve" } },                       // Portugal vs Algarve GP
            // 2024 special case
            { "barcelona", new[] { "solidarity", "motul" } },          // Catalunya vs Solidarity GP
        };

        /// <summary>
        /// Session type variations mapping - maps file session names to possible TVDB title patterns
        /// </summary>
        private static readonly Dictionary<string, string[]> SessionVariations = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Qualifying variations - generic
            { "qualifying", new[] { "qualifying", "q 1", "q 2", "q1", "q2", "qual" } },
            { "quali", new[] { "qualifying", "q 1", "q 2", "q1", "q2", "qual" } },
            { "qf", new[] { "qualifying", "q 1", "q 2", "q1", "q2", "qual" } },

            // Qualifying Q1 variations (multiple formats from parser)
            // Note: includes "qualifying" fallback for 2022 TVDB which only has generic "Qualifying" episodes
            { "q1", new[] { "q 1", "q1", "qualifying 1", "qualifying nr. 1", "qualifying one", "qualifying" } },
            { "qualifying q1", new[] { "q 1", "q1", "qualifying 1", "qualifying nr. 1", "qualifying one", "qualifying" } },
            { "qualifying one", new[] { "q 1", "q1", "qualifying 1", "qualifying nr. 1", "qualifying one", "qualifying" } },

            // Qualifying Q2 variations (multiple formats from parser)
            // Note: includes "qualifying" fallback for 2022 TVDB which only has generic "Qualifying" episodes
            { "q2", new[] { "q 2", "q2", "qualifying 2", "qualifying nr. 2", "qualifying two", "qualifying" } },
            { "qualifying q2", new[] { "q 2", "q2", "qualifying 2", "qualifying nr. 2", "qualifying two", "qualifying" } },
            { "qualifying two", new[] { "q 2", "q2", "qualifying 2", "qualifying nr. 2", "qualifying two", "qualifying" } },

            // Race variations
            { "race", new[] { "race" } },  // Note: exclude "sprint race" matches separately

            // Sprint variations
            { "sprint", new[] { "sprint race", "sprint" } },  // Note: exclude "sprint shootout/qualifying" matches separately
            { "sprint race", new[] { "sprint race", "sprint" } },
            { "sprint qualifying", new[] { "sprint qualifying", "sprint shootout" } },
            { "sprint shootout", new[] { "sprint shootout", "sprint qualifying" } },

            // Practice variations - FP format
            { "fp1", new[] { "free practice 1", "free practice nr. 1", "practice 1", "practice nr. 1", "fp 1", "fp1" } },
            { "fp2", new[] { "free practice 2", "free practice nr. 2", "practice 2", "practice nr. 2", "fp 2", "fp2" } },
            { "fp3", new[] { "free practice 3", "free practice nr. 3", "practice 3", "practice nr. 3", "fp 3", "fp3" } },
            { "fp4", new[] { "free practice 4", "free practice nr. 4", "practice 4", "practice nr. 4", "fp 4", "fp4" } },

            // Practice variations - practice format
            { "practice 1", new[] { "free practice 1", "free practice nr. 1", "practice 1", "practice nr. 1", "fp 1", "fp1" } },
            { "practice 2", new[] { "free practice 2", "free practice nr. 2", "practice 2", "practice nr. 2", "fp 2", "fp2" } },
            { "practice 3", new[] { "free practice 3", "free practice nr. 3", "practice 3", "practice nr. 3", "fp 3", "fp3" } },
            { "practice1", new[] { "free practice 1", "free practice nr. 1", "practice 1", "practice nr. 1", "fp 1", "fp1" } },
            { "practice2", new[] { "free practice 2", "free practice nr. 2", "practice 2", "practice nr. 2", "fp 2", "fp2" } },
            { "practice3", new[] { "free practice 3", "free practice nr. 3", "practice 3", "practice nr. 3", "fp 3", "fp3" } },
            { "practice", new[] { "practice", "free practice" } },

            // Warm up
            { "warmup", new[] { "warm up", "warmup", "warm-up" } },
            { "warm up", new[] { "warm up", "warmup", "warm-up" } },

            // Testing
            { "test", new[] { "test", "testing" } },
        };

        private Episode FindRacingEpisode(Series series, int season, string gpName, string sessionType, string racingClass)
        {
            _logger.Info("FindRacingEpisode called: Series='{0}', Season={1}, GP='{2}', Session='{3}', Class='{4}'",
                series.Title, season, gpName, sessionType, racingClass ?? "none");

            if (string.IsNullOrWhiteSpace(gpName) || string.IsNullOrWhiteSpace(sessionType))
            {
                _logger.Debug("Racing episode search requires both GP name and session type");
                return null;
            }

            var episodes = _episodeService.GetEpisodesBySeason(series.Id, season);

            if (!episodes.Any())
            {
                _logger.Warn("No episodes found for season {0} in series '{1}' (ID: {2})", season, series.Title, series.Id);
                return null;
            }

            _logger.Debug("Found {0} episodes in season {1}", episodes.Count, season);

            // Debug: Log episode titles that contain the GP name or class
            var gpNameLower = gpName.ToLower();
            var classLower = racingClass?.ToLower() ?? "";
            _logger.Info("DEBUG: Looking for GP '{0}' class '{1}' in {2} episodes", gpName, racingClass ?? "none", episodes.Count);
            foreach (var ep in episodes.Where(e => e.Title.ToLower().Contains(gpNameLower.Split(' ')[0]) ||
                                                    (!string.IsNullOrEmpty(classLower) && e.Title.ToLower().Contains(classLower))))
            {
                _logger.Info("DEBUG: Potential match - S{0:00}E{1:00}: {2}", ep.SeasonNumber, ep.EpisodeNumber, ep.Title);
            }

            // Step 1: Get location variations for the GP name
            var locationVariations = GetLocationVariations(gpName);
            _logger.Info("Location variations for '{0}': [{1}]", gpName, string.Join(", ", locationVariations));

            // Step 2: Get session variations
            var sessionVariations = GetSessionVariations(sessionType);
            _logger.Info("Session variations for '{0}': [{1}]", sessionType, string.Join(", ", sessionVariations));

            // Step 3: Find ALL episodes that match location AND session
            var matchingEpisodes = FindMatchingEpisodes(episodes, locationVariations, sessionVariations, racingClass, sessionType);
            _logger.Info("Found {0} matching episodes for GP '{1}' session '{2}' class '{3}'", matchingEpisodes.Count, gpName, sessionType, racingClass ?? "none");

            // Step 4: Handle results
            if (matchingEpisodes.Count == 0)
            {
                _logger.Warn("FAILURE: No matching racing episode found for GP '{0}' Session '{1}' in season {2}",
                    gpName, sessionType, season);
                LogSampleEpisodes(episodes);
                return null;
            }

            if (matchingEpisodes.Count == 1)
            {
                var episode = matchingEpisodes.First();
                _logger.Info("SUCCESS: Found racing episode: S{0:00}E{1:00} - {2}",
                    episode.SeasonNumber, episode.EpisodeNumber, episode.Title);
                return episode;
            }

            // Multiple matches - try to disambiguate
            _logger.Info("Multiple matches ({0}) found, attempting disambiguation for '{1}'",
                matchingEpisodes.Count, gpName);

            var disambiguated = DisambiguateDoubleHeader(matchingEpisodes, gpName);
            if (disambiguated != null)
            {
                _logger.Info("SUCCESS: Disambiguated to episode: S{0:00}E{1:00} - {2}",
                    disambiguated.SeasonNumber, disambiguated.EpisodeNumber, disambiguated.Title);
                return disambiguated;
            }

            // Still ambiguous - log and return first match (or null for strict mode)
            _logger.Warn("AMBIGUOUS: Multiple episodes match and cannot disambiguate. Returning first match.");
            foreach (var ep in matchingEpisodes)
            {
                _logger.Info("  - Candidate: S{0:00}E{1:00} - {2}", ep.SeasonNumber, ep.EpisodeNumber, ep.Title);
            }

            return matchingEpisodes.First();
        }

        /// <summary>
        /// Get all location variations for a given GP name using strict word matching
        /// </summary>
        private List<string> GetLocationVariations(string gpName)
        {
            var normalizedGpName = gpName.CleanSeriesTitle().ToLower();
            var variations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalizedGpName, gpName.ToLower() };

            // Split GP name into words and look up each word separately
            var gpWords = gpName.ToLower().Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

            // Generic country names that should be EXCLUDED if a specific circuit is found
            // This prevents "Spain Aragon" from matching "Spanish Grand Prix" (Jerez)
            var genericCountryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "spain", "spanish", "españa", "espana",
                "italy", "italian", "italia",
                "germany", "german", "deutschland",
                "japan", "japanese"
            };

            // Specific circuit names - if found, skip generic country variations
            var specificCircuitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "aragon", "aragón", "valencia", "catalunya", "catalonia", "barcelona", "jerez",
                "mugello", "imola", "misano",
                "hockenheim", "sachsenring", "nurburgring",
                "suzuka", "motegi"
            };

            // Check if GP name contains a specific circuit
            var hasSpecificCircuit = gpWords.Any(word => specificCircuitKeys.Contains(word));

            // Find matching keys in GpNameVariations using STRICT matching
            foreach (var kvp in GpNameVariations)
            {
                var cleanKey = kvp.Key.CleanSeriesTitle().ToLower();

                // Skip generic country keys if we have a specific circuit
                if (hasSpecificCircuit && genericCountryKeys.Contains(cleanKey))
                {
                    continue;
                }

                // Exact match on full name
                if (string.Equals(normalizedGpName, cleanKey, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var v in kvp.Value) variations.Add(v.ToLower());
                    continue;
                }

                // Check if any individual word from GP name matches dictionary key exactly
                if (gpWords.Any(word => string.Equals(word, cleanKey, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var v in kvp.Value) variations.Add(v.ToLower());
                    continue;
                }

                // Word boundary match - ensures "australia" doesn't match "austria" key
                try
                {
                    var keyPattern = $@"\b{Regex.Escape(cleanKey)}\b";
                    var gpPattern = $@"\b{Regex.Escape(normalizedGpName)}\b";

                    if (Regex.IsMatch(normalizedGpName, keyPattern, RegexOptions.IgnoreCase) ||
                        Regex.IsMatch(cleanKey, gpPattern, RegexOptions.IgnoreCase))
                    {
                        foreach (var v in kvp.Value) variations.Add(v.ToLower());
                    }
                }
                catch (ArgumentException)
                {
                    // Regex failed, skip this key
                }
            }

            return variations.ToList();
        }

        /// <summary>
        /// Get all session variations for a given session type
        /// </summary>
        private List<string> GetSessionVariations(string sessionType)
        {
            var normalizedSession = sessionType.ToLower().Replace(".", " ").Replace("_", " ").Replace("-", " ").Trim();
            // Collapse multiple spaces into single space
            normalizedSession = Regex.Replace(normalizedSession, @"\s+", " ");
            var variations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalizedSession };

            // Handle "quali" -> "qualifying"
            if (normalizedSession == "quali" || normalizedSession == "qf")
            {
                normalizedSession = "qualifying";
                variations.Add("qualifying");
            }

            // Look up in SessionVariations dictionary
            if (SessionVariations.TryGetValue(normalizedSession, out var sessionVars))
            {
                foreach (var v in sessionVars) variations.Add(v.ToLower());
            }

            // Handle compound FP patterns like "fp1", "fp 1", "practice1", "practice 1"
            var fpMatch = Regex.Match(normalizedSession, @"^(?:fp|practice)\s*(\d)$", RegexOptions.IgnoreCase);
            if (fpMatch.Success)
            {
                var num = fpMatch.Groups[1].Value;
                variations.Add($"fp {num}");
                variations.Add($"fp{num}");
                variations.Add($"practice {num}");
                variations.Add($"practice{num}");
                variations.Add($"free practice {num}");
                variations.Add($"free practice nr. {num}");
                variations.Add($"practice nr. {num}");
            }

            // Handle compound qualifying patterns like "qualifying q1", "qualifying one", etc.
            var qualMatch = Regex.Match(normalizedSession, @"^qual(?:ifying)?\s*(q)?(\d|one|two)$", RegexOptions.IgnoreCase);
            if (qualMatch.Success)
            {
                var numPart = qualMatch.Groups[2].Value.ToLower();
                var num = numPart == "one" ? "1" : numPart == "two" ? "2" : numPart;

                variations.Add($"q {num}");
                variations.Add($"q{num}");
                variations.Add($"qualifying {num}");
                variations.Add($"qualifying nr. {num}");
                variations.Add(num == "1" ? "qualifying one" : "qualifying two");
            }

            return variations.ToList();
        }

        /// <summary>
        /// Find all episodes matching the location and session criteria
        /// </summary>
        private List<Episode> FindMatchingEpisodes(List<Episode> episodes, List<string> locationVariations,
            List<string> sessionVariations, string racingClass, string originalSession)
        {
            var matches = new List<Episode>();
            var normalizedOriginalSession = originalSession.ToLower();
            var isPlainRace = normalizedOriginalSession == "race";
            var isPlainSprint = normalizedOriginalSession == "sprint" || normalizedOriginalSession == "sprint race";

            foreach (var episode in episodes)
            {
                var titleLower = episode.Title.ToLower();
                var titleClean = episode.Title.CleanSeriesTitle().ToLower();

                // Check location match using word boundary
                var hasLocationMatch = locationVariations.Any(loc =>
                {
                    // Try word boundary match on original title
                    try
                    {
                        var pattern = $@"\b{Regex.Escape(loc)}\b";
                        if (Regex.IsMatch(titleLower, pattern, RegexOptions.IgnoreCase))
                            return true;
                    }
                    catch { }

                    // For longer terms (5+ chars), also try contains on cleaned title
                    if (loc.Length >= 5 && titleClean.Contains(loc))
                        return true;

                    return false;
                });

                if (!hasLocationMatch) continue;

                // Check session match
                var hasSessionMatch = sessionVariations.Any(sess => titleLower.Contains(sess));

                // Special handling for "Race" - TVDB 2022 and earlier don't include "race" in race episode titles
                // e.g., "13.Motorrad Grand Prix von Österreich (MOTO2)" is a race episode
                // Match if: (1) no session keyword found, AND (2) this is a "race" search, AND
                // (3) title doesn't contain any other session type
                if (!hasSessionMatch && isPlainRace)
                {
                    var nonRaceSessionKeywords = new[] { "qualifying", "practice", "free practice", "sprint", "warm up", "test", "fp1", "fp2", "fp3", "fp4", "q1", "q2" };
                    var hasNonRaceKeyword = nonRaceSessionKeywords.Any(kw => titleLower.Contains(kw));
                    if (!hasNonRaceKeyword)
                    {
                        // This is likely a race episode without "race" in title
                        hasSessionMatch = true;
                    }
                }

                if (!hasSessionMatch)
                {
                    continue;
                }

                // Apply exclusions for plain "race" to avoid matching "sprint race"
                if (isPlainRace && titleLower.Contains("sprint")) continue;

                // Apply exclusions for "sprint"/"sprint race" to avoid matching "sprint shootout"/"sprint qualifying"
                if (isPlainSprint && (titleLower.Contains("shootout") || titleLower.Contains("qualifying"))) continue;

                // Check racing class if specified (MotoGP/Moto2/Moto3)
                // Handle variations: moto2, moto 2, MOTO2, Moto 2, Moto GP, etc.
                if (!string.IsNullOrEmpty(racingClass))
                {
                    var classLower = racingClass.ToLower();
                    // Try direct match first (moto2, moto3, motogp)
                    var hasClassMatch = titleLower.Contains(classLower);

                    // If no direct match, try with space before last char (moto 2, moto 3)
                    if (!hasClassMatch && classLower.Length > 4)
                    {
                        var classWithSpace = classLower.Insert(classLower.Length - 1, " ");
                        hasClassMatch = titleLower.Contains(classWithSpace);
                    }

                    // Special handling for "motogp" - also try "moto gp" (space before "gp")
                    if (!hasClassMatch && classLower == "motogp")
                    {
                        hasClassMatch = titleLower.Contains("moto gp");
                    }

                    // Also try with space after "moto" for moto2/moto3
                    if (!hasClassMatch && (classLower == "moto2" || classLower == "moto3"))
                    {
                        var classWithSpace = classLower.Insert(4, " "); // "moto 2" or "moto 3"
                        hasClassMatch = titleLower.Contains(classWithSpace);
                    }

                    // For MotoGP class: In 2025+ TVDB format, MotoGP episodes don't have class in title
                    // e.g., "SPAIN - Valencia - Q 1" is MotoGP, while Moto2/Moto3 have explicit class
                    // So if we're looking for MotoGP and title has no class, it's a MotoGP episode
                    if (!hasClassMatch && classLower == "motogp")
                    {
                        var hasAnyClassInTitle = titleLower.Contains("moto2") || titleLower.Contains("moto 2") ||
                                                  titleLower.Contains("moto3") || titleLower.Contains("moto 3") ||
                                                  titleLower.Contains("motogp") || titleLower.Contains("moto gp");
                        // If title has no class mentioned, and we're looking for MotoGP, it's likely the MotoGP episode
                        if (!hasAnyClassInTitle)
                        {
                            hasClassMatch = true;
                        }
                    }

                    if (!hasClassMatch)
                    {
                        continue;
                    }
                }

                matches.Add(episode);
            }

            return matches;
        }

        /// <summary>
        /// Try to disambiguate between multiple matching episodes (double-header scenario)
        /// </summary>
        private Episode DisambiguateDoubleHeader(List<Episode> candidates, string gpName)
        {
            var gpNameLower = gpName.ToLower();

            // Check if the GP name contains a disambiguating term
            foreach (var kvp in DoubleHeaderDisambiguators)
            {
                foreach (var disambiguator in kvp.Value)
                {
                    if (gpNameLower.Contains(disambiguator.ToLower()))
                    {
                        // Find the episode that contains this disambiguator
                        var match = candidates.FirstOrDefault(ep =>
                            ep.Title.ToLower().Contains(disambiguator.ToLower()));

                        if (match != null)
                        {
                            _logger.Debug("Disambiguated using term '{0}'", disambiguator);
                            return match;
                        }
                    }
                }
            }

            // TODO: Add file date disambiguation here if needed
            // For now, return null to indicate we couldn't disambiguate
            return null;
        }

        /// <summary>
        /// Log sample episode titles for debugging
        /// </summary>
        private void LogSampleEpisodes(List<Episode> episodes)
        {
            _logger.Debug("Searched {0} episodes. Sample episode titles:", episodes.Count);
            foreach (var ep in episodes.Take(5))
            {
                _logger.Debug("  - S{0:00}E{1:00}: {2}", ep.SeasonNumber, ep.EpisodeNumber, ep.Title);
            }
            if (episodes.Count > 5)
            {
                _logger.Debug("  ... and {0} more episodes", episodes.Count - 5);
            }
        }
    }
}
