using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Validation;
using Sonarr.Http;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace Sonarr.Api.V3.Indexers
{
    [V3ApiController]
    public class ReleaseController : ReleaseControllerBase
    {
        private readonly IFetchAndParseRss _rssFetcherAndParser;
        private readonly ISearchForReleases _releaseSearchService;
        private readonly IMakeDownloadDecision _downloadDecisionMaker;
        private readonly IPrioritizeDownloadDecision _prioritizeDownloadDecision;
        private readonly IDownloadService _downloadService;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly IParsingService _parsingService;
        private readonly Logger _logger;

        private readonly ICached<RemoteEpisode> _remoteEpisodeCache;

        public ReleaseController(IFetchAndParseRss rssFetcherAndParser,
                             ISearchForReleases releaseSearchService,
                             IMakeDownloadDecision downloadDecisionMaker,
                             IPrioritizeDownloadDecision prioritizeDownloadDecision,
                             IDownloadService downloadService,
                             ISeriesService seriesService,
                             IEpisodeService episodeService,
                             IParsingService parsingService,
                             ICacheManager cacheManager,
                             IQualityProfileService qualityProfileService,
                             Logger logger)
            : base(qualityProfileService)
        {
            _rssFetcherAndParser = rssFetcherAndParser;
            _releaseSearchService = releaseSearchService;
            _downloadDecisionMaker = downloadDecisionMaker;
            _prioritizeDownloadDecision = prioritizeDownloadDecision;
            _downloadService = downloadService;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _parsingService = parsingService;
            _logger = logger;

            _logger.Info("ReleaseController initialized - VERSION 2025-11-26-A");

            PostValidator.RuleFor(s => s.IndexerId).ValidId();
            PostValidator.RuleFor(s => s.Guid).NotEmpty();

            _remoteEpisodeCache = cacheManager.GetCache<RemoteEpisode>(GetType(), "remoteEpisodes");
        }

        [HttpPost]
        [Consumes("application/json")]
        public async Task<object> DownloadRelease([FromBody] ReleaseResource release)
        {
            _logger.Info("DownloadRelease called: EpisodeId={0}, EpisodeIds={1}, SeriesId={2}, ShouldOverride={3}, Guid={4}",
                release.EpisodeId?.ToString() ?? "null",
                release.EpisodeIds != null ? string.Join(",", release.EpisodeIds) : "null",
                release.SeriesId?.ToString() ?? "null",
                release.ShouldOverride?.ToString() ?? "null",
                release.Guid ?? "null");

            var remoteEpisode = _remoteEpisodeCache.Find(GetCacheKey(release));

            if (remoteEpisode == null)
            {
                _logger.Debug("Couldn't find requested release in cache, cache timeout probably expired.");

                throw new NzbDroneClientException(HttpStatusCode.NotFound, "Couldn't find requested release in cache, try searching again");
            }

            try
            {
                if (release.ShouldOverride == true)
                {
                    Ensure.That(release.SeriesId, () => release.SeriesId).IsNotNull();
                    Ensure.That(release.EpisodeIds, () => release.EpisodeIds).IsNotNull();
                    Ensure.That(release.EpisodeIds, () => release.EpisodeIds).HasItems();
                    Ensure.That(release.Quality, () => release.Quality).IsNotNull();
                    Ensure.That(release.Languages, () => release.Languages).IsNotNull();

                    // Clone the remote episode so we don't overwrite anything on the original
                    remoteEpisode = new RemoteEpisode
                    {
                        Release = remoteEpisode.Release,
                        ParsedEpisodeInfo = remoteEpisode.ParsedEpisodeInfo.JsonClone(),
                        SceneMapping = remoteEpisode.SceneMapping,
                        MappedSeasonNumber = remoteEpisode.MappedSeasonNumber,
                        EpisodeRequested = remoteEpisode.EpisodeRequested,
                        DownloadAllowed = remoteEpisode.DownloadAllowed,
                        SeedConfiguration = remoteEpisode.SeedConfiguration,
                        CustomFormats = remoteEpisode.CustomFormats,
                        CustomFormatScore = remoteEpisode.CustomFormatScore,
                        SeriesMatchType = remoteEpisode.SeriesMatchType,
                        ReleaseSource = remoteEpisode.ReleaseSource
                    };

                    remoteEpisode.Series = _seriesService.GetSeries(release.SeriesId!.Value);
                    remoteEpisode.Episodes = _episodeService.GetEpisodes(release.EpisodeIds);
                    remoteEpisode.ParsedEpisodeInfo.Quality = release.Quality;
                    remoteEpisode.Languages = release.Languages;
                }

                // CRITICAL: When release.EpisodeId is provided (from Interactive Search), ALWAYS use it
                // to override whatever episodes the parser found. This ensures the user's explicit
                // episode selection is respected, even if the parser matched a different episode
                // (e.g., racing content where "Qatar" in the title matches multiple GPs due to sponsor names)
                if (release.EpisodeId.HasValue)
                {
                    var episode = _episodeService.GetEpisode(release.EpisodeId.Value);
                    remoteEpisode.Series = _seriesService.GetSeries(episode.SeriesId);
                    remoteEpisode.Episodes = new List<Episode> { episode };
                    _logger.Info("Using episode from Interactive Search (override): {0} - {1}", episode.Id, episode.Title);
                }
                else if (remoteEpisode.Series == null)
                {
                    if (release.SeriesId.HasValue)
                    {
                        var series = _seriesService.GetSeries(release.SeriesId.Value);
                        var episodes = _parsingService.GetEpisodes(remoteEpisode.ParsedEpisodeInfo, series, true);

                        if (episodes.Empty())
                        {
                            throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to parse episodes in the release, will need to be manually provided");
                        }

                        remoteEpisode.Series = series;
                        remoteEpisode.Episodes = episodes;
                    }
                    else
                    {
                        throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to find matching series and episodes, will need to be manually provided");
                    }
                }
                else if (remoteEpisode.Episodes.Empty())
                {
                    List<Episode> episodes;

                    // When release.EpisodeId is provided (from Interactive Search), prioritize it
                    // over parser results. This ensures the user's explicit episode selection is
                    // respected, even if the parser matches a different episode (e.g., racing content
                    // where "Qatar" in the title matches multiple GPs due to sponsor names)
                    if (release.EpisodeId.HasValue)
                    {
                        var episode = _episodeService.GetEpisode(release.EpisodeId.Value);
                        episodes = new List<Episode> { episode };
                        _logger.Info("Using episode from Interactive Search: {0} - {1}", episode.Id, episode.Title);
                    }
                    else
                    {
                        episodes = _parsingService.GetEpisodes(remoteEpisode.ParsedEpisodeInfo, remoteEpisode.Series, true);
                    }

                    remoteEpisode.Episodes = episodes;
                }

                if (remoteEpisode.Episodes.Empty())
                {
                    throw new NzbDroneClientException(HttpStatusCode.NotFound, "Unable to parse episodes in the release, will need to be manually provided");
                }

                await _downloadService.DownloadReport(remoteEpisode, release.DownloadClientId);
            }
            catch (ReleaseDownloadException ex)
            {
                _logger.Error(ex, ex.Message);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Getting release from indexer failed");
            }

            return release;
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<ReleaseResource>> GetReleases(int? seriesId, int? episodeId, int? seasonNumber, string query = null)
        {
            if (episodeId.HasValue)
            {
                return await GetEpisodeReleases(episodeId.Value, query);
            }

            if (seriesId.HasValue && seasonNumber.HasValue)
            {
                return await GetSeasonReleases(seriesId.Value, seasonNumber.Value, query);
            }

            return await GetRss();
        }

        private async Task<List<ReleaseResource>> GetEpisodeReleases(int episodeId, string customQuery = null)
        {
            _logger.Info("API: GetEpisodeReleases called for episodeId={0}, customQuery={1}", episodeId, customQuery ?? "(null)");
            try
            {
                List<DownloadDecision> decisions;

                if (!string.IsNullOrWhiteSpace(customQuery))
                {
                    // Use custom query search
                    decisions = await _releaseSearchService.CustomQuerySearch(episodeId, customQuery, true, true);
                    _logger.Info("API: CustomQuerySearch returned {0} decisions", decisions.Count);
                }
                else
                {
                    // Use standard episode search
                    decisions = await _releaseSearchService.EpisodeSearch(episodeId, true, true);
                    _logger.Info("API: EpisodeSearch returned {0} decisions", decisions.Count);
                }

                var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);
                _logger.Info("API: PrioritizeDecisions returned {0} prioritized decisions", prioritizedDecisions.Count);

                // Check if there's a search warning (e.g., racing session type not detected)
                var searchWarning = NzbDrone.Core.IndexerSearch.ReleaseSearchService.GetSearchWarning(episodeId);
                _logger.Info("API: GetSearchWarning({0}) returned: '{1}'", episodeId, searchWarning ?? "(null)");
                if (!string.IsNullOrWhiteSpace(searchWarning))
                {
                    _logger.Info("API: Adding X-Search-Warning header: {0}", searchWarning);
                    Response.Headers.Add("X-Search-Warning", searchWarning);
                }

                // Check if there's search info (e.g., successful query terms)
                var searchInfo = NzbDrone.Core.IndexerSearch.ReleaseSearchService.GetSearchInfo(episodeId);
                _logger.Info("API: GetSearchInfo({0}) returned: '{1}'", episodeId, searchInfo ?? "(null)");
                if (!string.IsNullOrWhiteSpace(searchInfo))
                {
                    _logger.Info("API: Adding X-Search-Info header: {0}", searchInfo);
                    Response.Headers.Add("X-Search-Info", searchInfo);
                }

                return MapDecisions(prioritizedDecisions);
            }
            catch (SearchFailedException ex)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Episode search failed: " + ex.Message);
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private async Task<List<ReleaseResource>> GetSeasonReleases(int seriesId, int seasonNumber, string customQuery = null)
        {
            _logger.Info("API: GetSeasonReleases called for seriesId={0}, seasonNumber={1}, customQuery={2}", seriesId, seasonNumber, customQuery ?? "(null)");
            try
            {
                List<DownloadDecision> decisions;

                if (!string.IsNullOrWhiteSpace(customQuery))
                {
                    // Use custom query search for the season
                    decisions = await _releaseSearchService.CustomQuerySeasonSearch(seriesId, seasonNumber, customQuery, true, true);
                    _logger.Info("API: CustomQuerySeasonSearch returned {0} decisions", decisions.Count);
                }
                else
                {
                    // Use standard season search
                    decisions = await _releaseSearchService.SeasonSearch(seriesId, seasonNumber, false, false, true, true);
                    _logger.Info("API: SeasonSearch returned {0} decisions", decisions.Count);
                }

                var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);
                _logger.Info("API: PrioritizeDecisions returned {0} prioritized decisions", prioritizedDecisions.Count);

                // Check if there's search info for season search
                var searchInfo = NzbDrone.Core.IndexerSearch.ReleaseSearchService.GetSeasonSearchInfo(seriesId, seasonNumber);
                _logger.Info("API: GetSeasonSearchInfo({0}, {1}) returned: '{2}'", seriesId, seasonNumber, searchInfo ?? "(null)");
                if (!string.IsNullOrWhiteSpace(searchInfo))
                {
                    _logger.Info("API: Adding X-Search-Info header: {0}", searchInfo);
                    Response.Headers.Add("X-Search-Info", searchInfo);
                }

                return MapDecisions(prioritizedDecisions);
            }
            catch (SearchFailedException ex)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Season search failed: " + ex.Message);
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private async Task<List<ReleaseResource>> GetRss()
        {
            var reports = await _rssFetcherAndParser.Fetch();
            var decisions = _downloadDecisionMaker.GetRssDecision(reports);
            var prioritizedDecisions = _prioritizeDownloadDecision.PrioritizeDecisions(decisions);

            return MapDecisions(prioritizedDecisions);
        }

        protected override ReleaseResource MapDecision(DownloadDecision decision, int initialWeight)
        {
            var resource = base.MapDecision(decision, initialWeight);
            _remoteEpisodeCache.Set(GetCacheKey(resource), decision.RemoteEpisode, TimeSpan.FromMinutes(30));

            return resource;
        }

        private string GetCacheKey(ReleaseResource resource)
        {
            return string.Concat(resource.IndexerId, "_", resource.Guid);
        }
    }
}
