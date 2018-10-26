﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.TerrainFeatures;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;
using static DeepWoodsMod.DeepWoodsEnterExit;
using static DeepWoodsMod.DeepWoodsGlobals;
using static DeepWoodsMod.DeepWoodsSettings;

namespace DeepWoodsMod
{
    class DeepWoodsManager
    {
        public static DeepWoods currentDeepWoods = null;
        public static string currentWarpRequestName = null;
        public static Vector2? currentWarpRequestLocation = null;


        private static DeepWoods rootDeepWoodsBackup = null;
        private static bool lostMessageDisplayedToday = false;
        private static int nextRandomizeTime = 0;

        public static void WarpFarmerIntoDeepWoods(int level)
        {
            // Warp into root level if appropriate.
            if (level <= 1)
            {
                Game1.warpFarmer("DeepWoods", Settings.Map.RootLevelEnterLocation.X, Settings.Map.RootLevelEnterLocation.Y, false);
            }
            else if (!Game1.IsMasterGame)
            {
                Game1.MasterPlayer.queueMessage(Settings.Network.DeepWoodsMessageId, Game1.player, new object[] { NETWORK_MESSAGE_DEEPWOODS_WARP, level, "", new Vector2() });
            }
            else
            {
                WarpFarmerIntoDeepWoods(AddDeepWoodsFromObelisk(level));
            }
        }

        public static DeepWoods AddDeepWoodsFromObelisk(int level)
        {
            if (!Game1.IsMasterGame)
                throw new ApplicationException("Illegal call to DeepWoodsManager.AddDeepWoodsFromObelisk in client.");

            // First check if a level already exists and use that.
            foreach (GameLocation gameLocation in Game1.locations)
            {
                if (gameLocation is DeepWoods && (gameLocation as DeepWoods).level.Value == level)
                {
                    return gameLocation as DeepWoods;
                }
            }

            // Otherwise create a new level.
            DeepWoods deepWoods = new DeepWoods(level);
            DeepWoodsManager.AddDeepWoodsToGameLocations(deepWoods);
            return deepWoods;
        }

        public static void WarpFarmerIntoDeepWoodsFromServerObelisk(string name, Vector2 enterLocation)
        {
            Game1.player.FacingDirection = DeepWoodsEnterExit.EnterDirToFacingDirection(EnterDirection.FROM_TOP);
            Game1.warpFarmer(name, (int)enterLocation.X, (int)enterLocation.Y + 1, false);
        }

        public static void WarpFarmerIntoDeepWoods(DeepWoods deepWoods)
        {
            if (deepWoods == null)
                return;

            Game1.player.FacingDirection = DeepWoodsEnterExit.EnterDirToFacingDirection(deepWoods.EnterDir);
            if (deepWoods.EnterDir == EnterDirection.FROM_TOP)
            {
                Game1.warpFarmer(deepWoods.Name, deepWoods.enterLocation.X, deepWoods.enterLocation.Y + 1, false);
            }
            else if (deepWoods.EnterDir == EnterDirection.FROM_RIGHT)
            {
                Game1.warpFarmer(deepWoods.Name, deepWoods.enterLocation.X + 1, deepWoods.enterLocation.Y, false);
            }
            else
            {
                Game1.warpFarmer(deepWoods.Name, deepWoods.enterLocation.X, deepWoods.enterLocation.Y, false);
            }
        }

        public static void AddDeepWoodsToGameLocations(DeepWoods deepWoods)
        {
            Game1.locations.Add(deepWoods);

            if (Game1.IsMasterGame)
            {
                foreach (Farmer who in Game1.otherFarmers.Values)
                    if (who != Game1.player)
                        who.queueMessage(Settings.Network.DeepWoodsMessageId, Game1.MasterPlayer, new object[] { NETWORK_MESSAGE_DEEPWOODS_ADDREMOVE, (byte)1, deepWoods.Name });
            }
        }

        public static void RemoveDeepWoodsFromGameLocations(DeepWoods deepWoods)
        {
            // Player might be in this level, teleport them out
            if (Game1.player.currentLocation == deepWoods)
            {
                Game1.warpFarmer(Game1.getLocationRequest("Woods", false), WOODS_WARP_LOCATION.X, WOODS_WARP_LOCATION.Y, 0);
                // Take away all health and energy to avoid cheaters using Save Anywhere to escape getting lost
                if (deepWoods.level > 1 && deepWoods.IsLost())
                {
                    Game1.player.health = 1;
                    Game1.player.Stamina = 0;
                }
                Game1.player.currentLocation = Game1.getLocationFromName("Woods");
                Game1.player.Position = new Vector2(WOODS_WARP_LOCATION.X * 64, WOODS_WARP_LOCATION.Y * 64);
            }

            Game1.locations.Remove(deepWoods);

            if (Game1.IsMasterGame)
            {
                foreach (Farmer who in Game1.otherFarmers.Values)
                    if (who != Game1.player)
                        who.queueMessage(Settings.Network.DeepWoodsMessageId, Game1.MasterPlayer, new object[] { NETWORK_MESSAGE_DEEPWOODS_ADDREMOVE, (byte)0, deepWoods.Name });
            }
        }

        public static void AddBlankDeepWoodsToGameLocations(string name)
        {
            if (Game1.IsMasterGame)
                return;

            if (Game1.getLocationFromName(name) == null)
                AddDeepWoodsToGameLocations(new DeepWoods(name));
        }

        public static void RemoveDeepWoodsFromGameLocations(string name)
        {
            if (Game1.getLocationFromName(name) is DeepWoods deepWoods)
                RemoveDeepWoodsFromGameLocations(deepWoods);
        }

        public static void Remove()
        {
            if (!Game1.IsMasterGame)
                return;

            DeepWoodsManager.rootDeepWoodsBackup = Game1.getLocationFromName("DeepWoods") as DeepWoods;
            List<DeepWoods> toBeRemoved = new List<DeepWoods>();
            foreach (var location in Game1.locations)
            {
                if (location is DeepWoods deepWoods)
                    toBeRemoved.Add(deepWoods);
            }
            foreach (var deepWoods in toBeRemoved)
                DeepWoodsManager.RemoveDeepWoodsFromGameLocations(deepWoods);
        }

        private static void CheckValid()
        {
            if (!Game1.IsMasterGame)
                return;

            if (!IsValidForThisGame())
            {
                Remove();
                DeepWoodsManager.AddDeepWoodsToGameLocations(new DeepWoods(null, 1, EnterDirection.FROM_TOP));
            }
        }

        public static void Restore()
        {
            if (!Game1.IsMasterGame)
                return;

            DeepWoodsManager.AddDeepWoodsToGameLocations(DeepWoodsManager.rootDeepWoodsBackup);
            CheckValid();
            DeepWoodsManager.rootDeepWoodsBackup = null;
        }

        public static void Add()
        {
            if (!Game1.IsMasterGame)
                return;

            CheckValid();
        }

        public static void AddAll(List<string> deepWoodsLevelNames)
        {
            DeepWoodsManager.Remove();
            foreach (string name in deepWoodsLevelNames)
                AddBlankDeepWoodsToGameLocations(name);
        }

        public static bool IsValidForThisGame()
        {
            return (Game1.getLocationFromName("DeepWoods") is DeepWoods deepWoods
                && deepWoods.uniqueMultiplayerID.Value == Game1.MasterPlayer.UniqueMultiplayerID);
        }

        // This is called by every client at the start of a new day
        public static void LocalDayUpdate(int dayOfMonth)
        {
            DeepWoodsManager.currentDeepWoods = null;
            DeepWoodsManager.currentWarpRequestName = null;
            DeepWoodsManager.currentWarpRequestLocation = null;

            lostMessageDisplayedToday = false;

            if (Game1.IsMasterGame)
            {
                nextRandomizeTime = 0;
                Remove();
                Restore();
            }
        }

        // This is called by every client everytime the time of day changes (10 ingame minute intervals)
        public static void LocalTimeUpdate(int timeOfDay)
        {
            if (Game1.IsMasterGame)
            {
                CheckValid();

                // Check if it's a new hour
                if (timeOfDay >= nextRandomizeTime)
                {
                    // Loop over copies, because inside loops we remove and/or add DeepWoods levels from/to Game1.locations
                    foreach (var location in new List<GameLocation>(Game1.locations))
                    {
                        // Check which DeepWoods can be removed
                        if (location is DeepWoods deepWoods)
                            deepWoods.TryRemove();
                    }

                    foreach (var location in new List<GameLocation>(Game1.locations))
                    {
                        // Randomize exits
                        if (location is DeepWoods deepWoods)
                            deepWoods.RandomizeExits();
                    }

                    foreach (var location in new List<GameLocation>(Game1.locations))
                    {
                        // Validate exits
                        if (location is DeepWoods deepWoods)
                            deepWoods.ValidateAndIfNecessaryCreateExitChildren();
                    }

                    int timeTillNextRandomization = Game1.random.Next(3, 10) * 10;  // one of: 30, 40, 50, 60, 70, 80, 90
                    if (timeTillNextRandomization >= 60)
                        timeTillNextRandomization += 40;  // one of: 30, 40, 50, 100, 110, 120, 130

                    nextRandomizeTime = timeOfDay + timeTillNextRandomization;

                    // e.g. 1250 + 30 gives 1280, but we want 1320
                    if (nextRandomizeTime % 100 >= 60)
                        timeTillNextRandomization += 40;
                }
            }
        }

        // This is called by every client every frame
        public static void LocalTick()
        {
            DeepWoodsManager.currentDeepWoods?.CheckWarp();
        }

        public static void FixLighting()
        {
            if (!(Game1.currentLocation is DeepWoods || Game1.currentLocation is Woods))
            {
                if (Game1.timeOfDay < Game1.getStartingToGetDarkTime() && !Game1.isRaining)
                    Game1.outdoorLight = Color.White;
                return;
            }

            int darkOutDelta = Game1.timeOfDay - Game1.getTrulyDarkTime();
            if (darkOutDelta > 0)
            {
                double delta = darkOutDelta / 100 + (darkOutDelta % 100 / 60.0) + ((Game1.gameTimeInterval / (double)Game1.realMilliSecondsPerGameTenMinutes) / 6.0);
                double maxDelta = (2400 - Game1.getTrulyDarkTime()) / 100.0;

                double ratio = Math.Min(1.0, delta / maxDelta);

                if (ratio <= 0.0)
                {
                    Game1.ambientLight = DAY_LIGHT;
                }
                else if (ratio >= 1.0)
                {
                    Game1.ambientLight = NIGHT_LIGHT;
                }
                else
                {
                    Color dayLightFactorized = DAY_LIGHT * (float)(1.0 - ratio);
                    Color nightLightFactorized = NIGHT_LIGHT * (float)ratio;
                    Game1.ambientLight.R = (byte)Math.Min(255, dayLightFactorized.R + nightLightFactorized.R);
                    Game1.ambientLight.G = (byte)Math.Min(255, dayLightFactorized.G + nightLightFactorized.G);
                    Game1.ambientLight.B = (byte)Math.Min(255, dayLightFactorized.B + nightLightFactorized.B);
                    Game1.ambientLight.A = 255;
                }
            }
            else
            {
                Game1.ambientLight = DAY_LIGHT;
            }

            Game1.outdoorLight = Game1.ambientLight;
        }


        // Called whenever a player warps, both from and to may be null
        public static void PlayerWarped(Farmer who, DeepWoods from, DeepWoods to, GameLocation rawTo)
        {
            if (from is DeepWoods dw1 && to is DeepWoods dw2 && dw1.Name == dw2.Name)
                return;

            ModEntry.Log("PlayerWarped from: " + from?.Name + ", to: " + to?.Name, LogLevel.Debug);

            from?.RemovePlayer(who);
            to?.AddPlayer(who);

            if (from != null && to == null)
            {
                // We left the deepwoods, fix lighting
                DeepWoodsManager.FixLighting();

                // Stop music
                Game1.changeMusicTrack("none");
                Game1.updateMusic();

                // Workaround for bug where players are warped to [0,0] for some reason
                if (rawTo is Woods && who == Game1.player)
                {
                    who.Position = new Vector2(WOODS_WARP_LOCATION.X * 64, WOODS_WARP_LOCATION.Y * 64);
                }
            }

            if (who == Game1.player
                && from != null
                && to != null
                && from.Parent == null
                && to.Parent == from
                && !lostMessageDisplayedToday
                && !to.spawnedFromObelisk
                && ExitDirToEnterDir(CastEnterDirToExitDir(from.EnterDir)) == to.EnterDir)
            {
                Game1.addHUDMessage(new HUDMessage(I18N.LostMessage) { noIcon = true });
                lostMessageDisplayedToday = true;
            }

            if (who == Game1.player
                && to != null
                && to.level >= Settings.Level.MinLevelForWoodsObelisk
                && !Game1.player.hasOrWillReceiveMail(WOODS_OBELISK_WIZARD_MAIL_ID)
                && (Game1.player.mailReceived.Contains("hasPickedUpMagicInk") || Game1.player.hasMagicInk))
            {
                Game1.addMailForTomorrow(WOODS_OBELISK_WIZARD_MAIL_ID);
            }
        }
    }
}
