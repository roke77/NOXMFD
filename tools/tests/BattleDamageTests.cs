using NOXMFD;

namespace NOXMFD.Tests
{
    public class BattleDamageTests
    {
        // BattleDamage is static: each test spawns its own aircraft so the fitted latches start clean.
        private readonly object AircraftA = new object();
        private readonly object AircraftB = new object();

        [Fact]
        public void NeverFittedSystemIsNoneNotFail()
        {
            BattleDamage.Update(AircraftA, on: true, radarAlive: false, tgpAlive: true, laserAlive: true);
            Assert.Contains("\"radar\":\"none\"", BattleDamage.StateJson);
            Assert.Contains("\"tgp\":\"ok\"", BattleDamage.StateJson);
        }

        [Fact]
        public void LostSystemFailsAndRespawnRecovers()
        {
            BattleDamage.Update(AircraftA, on: true, radarAlive: true, tgpAlive: true, laserAlive: true);
            BattleDamage.Update(AircraftA, on: true, radarAlive: false, tgpAlive: true, laserAlive: false);
            Assert.Equal("{\"on\":true,\"radar\":\"fail\",\"tgp\":\"ok\",\"laser\":\"fail\"}", BattleDamage.StateJson);
            Assert.True(BattleDamage.RadarFailed);
            Assert.True(BattleDamage.LaserFailed);
            Assert.False(BattleDamage.TgpFailed);

            // A repaired component reads ok again; a new aircraft starts over.
            BattleDamage.Update(AircraftA, on: true, radarAlive: true, tgpAlive: true, laserAlive: false);
            Assert.Contains("\"radar\":\"ok\"", BattleDamage.StateJson);
            BattleDamage.Update(AircraftB, on: true, radarAlive: false, tgpAlive: true, laserAlive: false);
            Assert.Contains("\"radar\":\"none\"", BattleDamage.StateJson);
            Assert.Contains("\"laser\":\"none\"", BattleDamage.StateJson);
        }

        [Fact]
        public void LaserFailIsSubsumedByTgpFailAndOffDisablesBoth()
        {
            BattleDamage.Update(AircraftA, on: true, radarAlive: true, tgpAlive: true, laserAlive: true);
            BattleDamage.Update(AircraftA, on: true, radarAlive: true, tgpAlive: false, laserAlive: false);
            Assert.True(BattleDamage.TgpFailed);
            Assert.False(BattleDamage.LaserFailed);

            BattleDamage.Update(AircraftA, on: false, radarAlive: true, tgpAlive: false, laserAlive: false);
            Assert.False(BattleDamage.TgpFailed);
            Assert.StartsWith("{\"on\":false", BattleDamage.StateJson);
        }

        [Fact]
        public void NoAircraftReadsNone()
        {
            BattleDamage.Update(AircraftA, on: true, radarAlive: true, tgpAlive: true, laserAlive: true);
            BattleDamage.Update(null, on: true, radarAlive: true, tgpAlive: true, laserAlive: true);
            Assert.Equal("{\"on\":true,\"radar\":\"none\",\"tgp\":\"none\",\"laser\":\"none\"}", BattleDamage.StateJson);
        }
    }
}
