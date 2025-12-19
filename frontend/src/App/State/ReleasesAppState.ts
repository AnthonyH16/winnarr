import AppSectionState, {
  AppSectionFilterState,
} from 'App/State/AppSectionState';
import Release from 'typings/Release';

interface ReleasesAppState
  extends AppSectionState<Release>,
    AppSectionFilterState<Release> {
  searchWarning?: string | null;
  searchInfo?: string | null;
}

export default ReleasesAppState;
