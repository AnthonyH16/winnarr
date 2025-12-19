interface EpisodeSearchPayload {
  episodeId: number;
  query?: string;
}

interface SeasonSearchPayload {
  seriesId: number;
  seasonNumber: number;
  query?: string;
}

type InteractiveSearchPayload = EpisodeSearchPayload | SeasonSearchPayload;

export default InteractiveSearchPayload;
