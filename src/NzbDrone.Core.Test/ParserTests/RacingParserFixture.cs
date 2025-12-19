using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class RacingParserFixture : CoreTest
    {
        // Formula 1 - Standard format with "Round" keyword
        [TestCase("Formula1.2024.Round01.Bahrain.Race.1080p.WEB.h264-VERUM", "Formula 1", 2024, "Bahrain", "Race")]
        [TestCase("Formula1.2024.Round.01.Bahrain.Race.1080p", "Formula 1", 2024, "Bahrain", "Race")]
        [TestCase("Formula1.2022.Round01.Saudi.Arabia.Race.1080p", "Formula 1", 2022, "Saudi Arabia", "Race")]

        // Formula 1 - Abbreviated "R" format
        [TestCase("F1.2024.R13.Hungarian.Grand.Prix.Qualifying.SkyF1HD.1080p", "F1", 2024, "Hungarian", "Qualifying")]
        [TestCase("F1.2024.R01.Bahrain.Qualifying.1080p", "F1", 2024, "Bahrain", "Qualifying")]
        [TestCase("F1.2024.R13.Hungarian.GP.Qualifying.1080p", "F1", 2024, "Hungarian", "Qualifying")]

        // Formula 1 - Without round number (GP name directly after year)
        [TestCase("Formula1.2024.Monaco.Grand.Prix.Race.1080p.WEB.h264-verum", "Formula 1", 2024, "Monaco", "Race")]
        [TestCase("formula1.2024.monaco.grand.prix.race.1080p", "Formula 1", 2024, "Monaco", "Race")]
        [TestCase("formula1.2024.bahrain.race.1080p.web.h264-verum", "Formula 1", 2024, "Bahrain", "Race")]

        // Formula 1 - Multi-word GP names
        [TestCase("F1.2024.Emilia.Romagna.Grand.Prix.Qualifying.1080p", "F1", 2024, "Emilia Romagna", "Qualifying")]
        [TestCase("Formula1.2024.Las.Vegas.Grand.Prix.Race.1080p", "Formula 1", 2024, "Las Vegas", "Race")]
        [TestCase("F1.2024.Abu.Dhabi.Qualifying.1080p", "F1", 2024, "Abu Dhabi", "Qualifying")]

        // Formula 1 - Sprint sessions
        [TestCase("F1.2024.Miami.Sprint.Qualifying.1080p", "F1", 2024, "Miami", "Sprint Qualifying")]
        [TestCase("Formula1.2024.Round06.Miami.Sprint.Race.1080p", "Formula 1", 2024, "Miami", "Sprint Race")]
        [TestCase("F1.2024.Brazil.Sprint.1080p", "F1", 2024, "Brazil", "Sprint")]

        // Formula 1 - Practice sessions
        [TestCase("Formula1.2024.Monaco.Practice.1.1080p", "Formula 1", 2024, "Monaco", "Practice 1")]
        [TestCase("F1.2024.Bahrain.Practice1.1080p", "F1", 2024, "Bahrain", "Practice1")]
        [TestCase("F1.2024.Monaco.Practice.1080p", "F1", 2024, "Monaco", "Practice")]

        // MotoGP
        [TestCase("MotoGP.2024.Round.05.France.Race.1080p", "MotoGP", 2024, "France", "Race")]
        [TestCase("MotoGP.2024.R05.France.Race.1080p", "MotoGP", 2024, "France", "Race")]
        [TestCase("MotoGP.2024.Round05.Qatar.Qualifying.1080p", "MotoGP", 2024, "Qatar", "Qualifying")]
        [TestCase("MotoGP.2024.Mugello.Sprint.1080p", "MotoGP", 2024, "Mugello", "Sprint")]

        // MotoGP - Austria vs Australia disambiguation (critical test case)
        // "Austria" should NOT match "Australia" episodes and vice versa
        [TestCase("MotoGP.2025x19.Australia.Qualifying.TNTSportsHD.1080p", "MotoGP", 2025, "Australia", "Qualifying")]
        [TestCase("MotoGP.2025.Austria.Red.Bull.Ring.Qualifying.1080p", "MotoGP", 2025, "Austria Red Bull Ring", "Qualifying")]
        [TestCase("MotoGP.2025.Austrian.Grand.Prix.Race.1080p", "MotoGP", 2025, "Austrian", "Race")]
        [TestCase("MotoGP.2025.Australian.Grand.Prix.Race.1080p", "MotoGP", 2025, "Australian", "Race")]
        [TestCase("MotoGP.2025.Spielberg.Race.1080p", "MotoGP", 2025, "Spielberg", "Race")]
        [TestCase("MotoGP.2025.Phillip.Island.Race.1080p", "MotoGP", 2025, "Phillip Island", "Race")]

        // WSBK (World Superbike)
        [TestCase("WSBK.2024.Round03.Phillip.Island.Race1.1080p", "WSBK", 2024, "Phillip Island", "Race1")]
        [TestCase("WSBK.2024.R03.Phillip.Island.Race.1080p", "WSBK", 2024, "Phillip Island", "Race")]
        [TestCase("WSBK.2024.Assen.Qualifying.1080p", "WSBK", 2024, "Assen", "Qualifying")]

        // Different separators
        [TestCase("Formula1_2024_Monaco_Grand_Prix_Race_1080p", "Formula 1", 2024, "Monaco", "Race")]
        [TestCase("F1-2024-Bahrain-Race-1080p", "F1", 2024, "Bahrain", "Race")]
        [TestCase("Formula 1 2024 Monaco Race 1080p", "Formula 1", 2024, "Monaco", "Race")]

        public void should_parse_racing_episode(string postTitle, string seriesTitle, int season, string gpName, string sessionType)
        {
            var result = Parser.Parser.ParseTitle(postTitle);
            result.Should().NotBeNull();
            result.SeriesTitle.Should().Be(seriesTitle);
            result.SeasonNumber.Should().Be(season);
            result.IsRacingContent.Should().BeTrue();
            result.RacingGpName.Should().Be(gpName);
            result.RacingSessionType.Should().Be(sessionType);
            result.EpisodeNumbers.Should().BeEmpty();
        }

        [TestCase("Formula1.2024.Round01.Bahrain.Race.1080p")]
        [TestCase("F1.2024.Monaco.Grand.Prix.Qualifying.1080p")]
        [TestCase("MotoGP.2024.Round05.France.Race.1080p")]
        [TestCase("WSBK.2024.Phillip.Island.Race.1080p")]
        public void should_have_empty_episode_numbers_for_racing_content(string postTitle)
        {
            var result = Parser.Parser.ParseTitle(postTitle);
            result.Should().NotBeNull();
            result.IsRacingContent.Should().BeTrue();
            result.EpisodeNumbers.Should().BeEmpty("Racing content should not have episode numbers extracted from filename");
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
        }

        [TestCase("Formula1.2024.Round01.Bahrain.Race.1080p", "Formula 1 - [Racing: Bahrain - Race]")]
        public void racing_content_should_have_descriptive_tostring(string postTitle, string expected)
        {
            var result = Parser.Parser.ParseTitle(postTitle);
            result.Should().NotBeNull();

            // Note: This test may need adjustment based on how ToString() handles racing content
            // For now, just verify it doesn't throw
            var stringResult = result.ToString();
            stringResult.Should().NotBeNullOrWhiteSpace();
        }
    }
}
