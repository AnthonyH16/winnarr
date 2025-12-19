import React from 'react';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import translate from 'Utilities/String/translate';

function SeriesTypePopoverContent() {
  return (
    <DescriptionList>
      <DescriptionListItem
        title={translate('Anime')}
        data={translate('AnimeEpisodeTypeDescription')}
      />

      <DescriptionListItem
        title={translate('Daily')}
        data={translate('DailyEpisodeTypeDescription')}
      />

      <DescriptionListItem
        title={translate('Standard')}
        data={translate('StandardEpisodeTypeDescription')}
      />

      <DescriptionListItem
        title={translate('Racing')}
        data={translate('RacingEpisodeTypeDescription')}
      />
    </DescriptionList>
  );
}

export default SeriesTypePopoverContent;
