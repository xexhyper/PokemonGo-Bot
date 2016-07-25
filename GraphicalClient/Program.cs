using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AllEnum;
using PokemonGo.RocketAPI.Enums;
using PokemonGo.RocketAPI.Exceptions;
using PokemonGo.RocketAPI.Extensions;
using PokemonGo.RocketAPI.GeneratedCode;
using System.Net.Http;
using System.Text;
using Google.Protobuf;
using PokemonGo.RocketAPI.Helpers;
using System.IO;



namespace PokemonGo.RocketAPI.GUI
{
    internal class Program
    {
        public static bhelper.Hero _hero;

        private static async Task EvolveAllGivenPokemons(Client client, IEnumerable<PokemonData> pokemonToEvolve)
        {
            foreach (var pokemon in pokemonToEvolve)
            {
                /*
                enum Holoholo.Rpc.Types.EvolvePokemonOutProto.Result {
	                UNSET = 0;
	                SUCCESS = 1;
	                FAILED_POKEMON_MISSING = 2;
	                FAILED_INSUFFICIENT_RESOURCES = 3;
	                FAILED_POKEMON_CANNOT_EVOLVE = 4;
	                FAILED_POKEMON_IS_DEPLOYED = 5;
                }
                }*/

                var countOfEvolvedUnits = 0;
                var xpCount = 0;

                EvolvePokemonOut evolvePokemonOutProto;
                do
                {
                    evolvePokemonOutProto = await client.EvolvePokemon(pokemon.Id);
                    //todo: someone check whether this still works

                    if (evolvePokemonOutProto.Result == 1)
                    {
                        bhelper.Main.ColoredConsoleWrite(ConsoleColor.Cyan,
                            $"[{DateTime.Now.ToString("HH:mm:ss")}] Evolved {pokemon.PokemonId} successfully for {evolvePokemonOutProto.ExpAwarded}xp");

                        countOfEvolvedUnits++;
                        xpCount += evolvePokemonOutProto.ExpAwarded;
                    }
                    else
                    {
                        var result = evolvePokemonOutProto.Result;
                        /*
                        ColoredConsoleWrite(ConsoleColor.White, $"Failed to evolve {pokemon.PokemonId}. " +
                                                 $"EvolvePokemonOutProto.Result was {result}");

                        ColoredConsoleWrite(ConsoleColor.White, $"Due to above error, stopping evolving {pokemon.PokemonId}");
                        */
                    }
                } while (evolvePokemonOutProto.Result == 1);
                if (countOfEvolvedUnits > 0)
                    bhelper.Main.ColoredConsoleWrite(ConsoleColor.Cyan,
                        $"[{DateTime.Now.ToString("HH:mm:ss")}] Evolved {countOfEvolvedUnits} pieces of {pokemon.PokemonId} for {xpCount}xp");

                await Task.Delay(3000);
            }
        }

        public static async void Execute()
        {
            var client = new Client(_hero.ClientSettings);
            try
            {
                bhelper.Main.CheckVersion(Assembly.GetExecutingAssembly().GetName());
                if (_hero.ClientSettings.AuthType == AuthType.Ptc)
                    await client.DoPtcLogin(_hero.ClientSettings.PtcUsername, _hero.ClientSettings.PtcPassword);
                else if (_hero.ClientSettings.AuthType == AuthType.Google)
                    await client.DoGoogleLogin();

                await client.SetServer();
                var profile = await client.GetProfile();
                var settings = await client.GetSettings();
                var mapObjects = await client.GetMapObjects();
                var inventory = await client.GetInventory();
                var pokemons =
                    inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                        .Where(p => p != null && p?.PokemonId > 0);
                var stats = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData.PlayerStats).ToArray();
                foreach (var v in stats)
                    if (v != null)
                        _hero.TotalKmWalked = v.KmWalked;

                bhelper.Main.ColoredConsoleWrite(ConsoleColor.Yellow, "+-------------- account info ---------------+");
                bhelper.Main.ColoredConsoleWrite(ConsoleColor.DarkGray, " Name: " + profile.Profile.Username);
                bhelper.Main.ColoredConsoleWrite(ConsoleColor.DarkGray, " Team: " + profile.Profile.Team);
                bhelper.Main.ColoredConsoleWrite(ConsoleColor.DarkGray, " Stardust: " + profile.Profile.Currency.ToArray()[1].Amount);
                bhelper.Main.ColoredConsoleWrite(ConsoleColor.DarkGray, "Total km walked: " + _hero.TotalKmWalked);
                bhelper.Main.ColoredConsoleWrite(ConsoleColor.DarkGray, " Latitude: " + _hero.ClientSettings.DefaultLatitude);
                bhelper.Main.ColoredConsoleWrite(ConsoleColor.DarkGray, " Longitude: " + _hero.ClientSettings.DefaultLongitude);
                bhelper.Main.ColoredConsoleWrite(ConsoleColor.Yellow, "+--------------------------------------------+");
                if (_hero.ClientSettings.TransferType == "leaveStrongest")
                    await TransferAllButStrongestUnwantedPokemon(client);
                else if (_hero.ClientSettings.TransferType == "all")
                    await TransferAllGivenPokemons(client, pokemons);
                else if (_hero.ClientSettings.TransferType == "duplicate")
                    await TransferDuplicatePokemon(client);
                else if (_hero.ClientSettings.TransferType == "cp")
                    await TransferAllWeakPokemon(client, _hero.ClientSettings.TransferCPThreshold);
                else
                    bhelper.Main.ColoredConsoleWrite(ConsoleColor.DarkGray, $"[{DateTime.Now.ToString("HH:mm:ss")}] Transfering pokemon disabled");
                if (_hero.ClientSettings.EvolveAllGivenPokemons)
                    await EvolveAllGivenPokemons(client, pokemons);
                if (_hero.ClientSettings.Recycler)
                    client.RecycleItems(client);
                
                await Task.Delay(5000);

                PrintLevel(client);
                UpdateFormTitle(client);


                if (_hero.ClientSettings.EggHatchedOutput)
                    await bLogic.Pokemon.CheckEggsHatched(_hero);
                if (_hero.ClientSettings.UseLuckyEggMode == "always")
                    await client.UseLuckyEgg(client);

                await ExecuteFarmingPokestopsAndPokemons(client);
                bhelper.Main.ColoredConsoleWrite(ConsoleColor.Red, $"[{DateTime.Now.ToString("HH:mm:ss")}] No nearby usefull locations found. Please wait 10 seconds.");
                await Task.Delay(10000);
                Execute();
            }
            catch (TaskCanceledException) { bhelper.Main.ColoredConsoleWrite(ConsoleColor.White, "Task Canceled Exception - Restarting"); Execute(); }
            catch (UriFormatException) { bhelper.Main.ColoredConsoleWrite(ConsoleColor.White, "System URI Format Exception - Restarting"); Execute(); }
            catch (ArgumentOutOfRangeException) { bhelper.Main.ColoredConsoleWrite(ConsoleColor.White, "ArgumentOutOfRangeException - Restarting"); Execute(); }
            catch (ArgumentNullException) { bhelper.Main.ColoredConsoleWrite(ConsoleColor.White, "Argument Null Refference - Restarting"); Execute(); }
            catch (NullReferenceException) { bhelper.Main.ColoredConsoleWrite(ConsoleColor.White, "Null Refference - Restarting"); Execute(); }
            catch (Exception ex) { bhelper.Main.ColoredConsoleWrite(ConsoleColor.White, ex.ToString()); Execute(); }
        }

        public static async Task UpdateFormTitle(Client client)
        {
            var inventory = await client.GetInventory();
            var stats = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.PlayerStats).ToArray();
            var profile = await client.GetProfile();
            foreach (var v in stats)
                if (v != null)
                {
                    int XpDiff = bhelper.Game.GetXpDiff(v.Level);

                    MainWindow.main.Status = string.Format("Level: {0:0} - [{1:0}/ {2:0}] | Stardust: {3:0}", v.Level, (v.Experience - v.PrevLevelXp - XpDiff), (v.NextLevelXp - v.PrevLevelXp - XpDiff), profile.Profile.Currency.ToArray()[1].Amount);
                    
                }
            await Task.Delay(1000);
            UpdateFormTitle(client);
        }
        

        private static async Task ExecuteFarmingPokestopsAndPokemons(Client client)
        {
            var mapObjects = await client.GetMapObjects();

            var pokeStops = mapObjects.MapCells.SelectMany(i => i.Forts).Where(i => i.Type == FortType.Checkpoint && i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime());

            foreach (var pokeStop in pokeStops)
            {
                var update = await client.UpdatePlayerLocation(pokeStop.Latitude, pokeStop.Longitude);
                var fortInfo = await client.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                var fortSearch = await client.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);

                StringWriter PokeStopOutput = new StringWriter();
                PokeStopOutput.Write($"[{DateTime.Now.ToString("HH:mm:ss")}] ");
                if (fortInfo.Name != string.Empty)
                    PokeStopOutput.Write("PokeStop: " + fortInfo.Name);
                if (fortSearch.ExperienceAwarded != 0)
                    PokeStopOutput.Write($", XP: {fortSearch.ExperienceAwarded}");
                if (fortSearch.GemsAwarded != 0)
                    PokeStopOutput.Write($", Gems: {fortSearch.GemsAwarded}");
                if (fortSearch.PokemonDataEgg != null)
                    PokeStopOutput.Write($", Eggs: {fortSearch.PokemonDataEgg}");
                if (GetFriendlyItemsString(fortSearch.ItemsAwarded) != string.Empty)
                    PokeStopOutput.Write($", Items: {GetFriendlyItemsString(fortSearch.ItemsAwarded)} ");
                bhelper.Main.ColoredConsoleWrite(ConsoleColor.Cyan, PokeStopOutput.ToString());

                if (fortSearch.ExperienceAwarded != 0)
                    _hero.TotalExperience += (fortSearch.ExperienceAwarded);
                await Task.Delay(15000);
                await bLogic.Pokemon.ExecuteCatchAllNearbyPokemons(_hero);
            }
        }

        private static string GetFriendlyItemsString(IEnumerable<FortSearchResponse.Types.ItemAward> items)
        {
            var enumerable = items as IList<FortSearchResponse.Types.ItemAward> ?? items.ToList();

            if (!enumerable.Any())
                return string.Empty;

            return enumerable.GroupBy(i => i.ItemId)
                    .Select(kvp => new { ItemName = kvp.Key.ToString().Substring(4), Amount = kvp.Sum(x => x.ItemCount) })
                    .Select(y => $"{y.Amount}x {y.ItemName}")
                    .Aggregate((a, b) => $"{a}, {b}");
        }

        private static async Task TransferAllButStrongestUnwantedPokemon(Client client)
        {
            //ColoredConsoleWrite(ConsoleColor.White, $"[{DateTime.Now.ToString("HH:mm:ss")}] Firing up the meat grinder");

            PokemonId[] unwantedPokemonTypes = new[]
            {
                PokemonId.Pidgey,
                PokemonId.Rattata,
                PokemonId.Weedle,
                PokemonId.Zubat,
                PokemonId.Caterpie,
                PokemonId.Pidgeotto,
                PokemonId.Paras,
                PokemonId.Venonat,
                PokemonId.Psyduck,
                PokemonId.Poliwag,
                PokemonId.Slowpoke,
                PokemonId.Drowzee,
                PokemonId.Gastly,
                PokemonId.Goldeen,
                PokemonId.Staryu,
                PokemonId.Magikarp,
                PokemonId.Clefairy,
                PokemonId.Eevee,
                PokemonId.Tentacool,
                PokemonId.Dratini,
                PokemonId.Ekans,
                PokemonId.Jynx,
                PokemonId.Lickitung,
                PokemonId.Spearow,
                PokemonId.NidoranFemale,
                PokemonId.NidoranMale
            };

            var inventory = await client.GetInventory();
            var pokemons = inventory.InventoryDelta.InventoryItems
                .Select(i => i.InventoryItemData?.Pokemon)
                .Where(p => p != null && p?.PokemonId > 0)
                .ToArray();

            foreach (var unwantedPokemonType in unwantedPokemonTypes)
            {
                var pokemonOfDesiredType = pokemons.Where(p => p.PokemonId == unwantedPokemonType)
                    .OrderByDescending(p => p.Cp)
                    .ToList();

                var unwantedPokemon =
                    pokemonOfDesiredType.Skip(1) // keep the strongest one for potential battle-evolving
                        .ToList();

                //ColoredConsoleWrite(ConsoleColor.White, $"[{DateTime.Now.ToString("HH:mm:ss")}] Grinding {unwantedPokemon.Count} pokemons of type {unwantedPokemonType}");
                await TransferAllGivenPokemons(client, unwantedPokemon);
            }

            //ColoredConsoleWrite(ConsoleColor.White, $"[{DateTime.Now.ToString("HH:mm:ss")}] Finished grinding all the meat");
        }

        public static float Perfect(PokemonData poke)
        {
            return ((float)(poke.IndividualAttack + poke.IndividualDefense + poke.IndividualStamina) / (3.0f * 15.0f)) * 100.0f;
        }

        private static async Task TransferAllGivenPokemons(Client client, IEnumerable<PokemonData> unwantedPokemons, float keepPerfectPokemonLimit = 80.0f)
        {
            foreach (var pokemon in unwantedPokemons)
            {
                if (Perfect(pokemon) >= keepPerfectPokemonLimit) continue;
                bhelper.Main.ColoredConsoleWrite(ConsoleColor.White, $"[{DateTime.Now.ToString("HH:mm:ss")}] Pokemon {pokemon.PokemonId} with {pokemon.Cp} CP has IV percent less than {keepPerfectPokemonLimit}%");

                if (pokemon.Favorite == 0)
                {
                    var transferPokemonResponse = await client.TransferPokemon(pokemon.Id);

                    /*
                    ReleasePokemonOutProto.Status {
                        UNSET = 0;
                        SUCCESS = 1;
                        POKEMON_DEPLOYED = 2;
                        FAILED = 3;
                        ERROR_POKEMON_IS_EGG = 4;
                    }*/
                    string pokemonName;
                    if (_hero.ClientSettings.Language == "german")
                        pokemonName = Convert.ToString((PokemonId_german)(int)pokemon.PokemonId);
                    else
                        pokemonName = Convert.ToString(pokemon.PokemonId);
                    if (transferPokemonResponse.Status == 1)
                    {
                        bhelper.Main.ColoredConsoleWrite(ConsoleColor.Magenta, $"[{DateTime.Now.ToString("HH:mm:ss")}] Transferred {pokemonName} with {pokemon.Cp} CP");
                    }
                    else
                    {
                        var status = transferPokemonResponse.Status;

                        bhelper.Main.ColoredConsoleWrite(ConsoleColor.Red, $"[{DateTime.Now.ToString("HH:mm:ss")}] Somehow failed to transfer {pokemonName} with {pokemon.Cp} CP. " +
                                                 $"ReleasePokemonOutProto.Status was {status}");
                    }

                    await Task.Delay(3000);
                }
            }
        }

        private static async Task TransferDuplicatePokemon(Client client)
        {

            //ColoredConsoleWrite(ConsoleColor.White, $"Check for duplicates");
            var inventory = await client.GetInventory();
            var allpokemons =
                inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                    .Where(p => p != null && p?.PokemonId > 0);

            var dupes = allpokemons.OrderBy(x => x.Cp).Select((x, i) => new { index = i, value = x })
                .GroupBy(x => x.value.PokemonId)
                .Where(x => x.Skip(1).Any());

            for (var i = 0; i < dupes.Count(); i++)
            {
                for (var j = 0; j < dupes.ElementAt(i).Count() - 1; j++)
                {
                    var dubpokemon = dupes.ElementAt(i).ElementAt(j).value;
                    if (dubpokemon.Favorite == 0)
                    {
                        var transfer = await client.TransferPokemon(dubpokemon.Id);
                        string pokemonName;
                        if (_hero.ClientSettings.Language == "german")
                            pokemonName = Convert.ToString((PokemonId_german)(int)dubpokemon.PokemonId);
                        else
                            pokemonName = Convert.ToString(dubpokemon.PokemonId);
                        bhelper.Main.ColoredConsoleWrite(ConsoleColor.DarkGreen,
                            $"[{DateTime.Now.ToString("HH:mm:ss")}] Transferred {pokemonName} with {dubpokemon.Cp} CP (Highest is {dupes.ElementAt(i).Last().value.Cp})");

                    }
                }
            }
        }

        private static async Task TransferAllWeakPokemon(Client client, int cpThreshold)
        {
            //ColoredConsoleWrite(ConsoleColor.White, $"[{DateTime.Now.ToString("HH:mm:ss")}] Firing up the meat grinder");

            PokemonId[] doNotTransfer = new[] //these will not be transferred even when below the CP threshold
            { // DO NOT EMPTY THIS ARRAY
                //PokemonId.Pidgey,
                //PokemonId.Rattata,
                //PokemonId.Weedle,
                //PokemonId.Zubat,
                //PokemonId.Caterpie,
                //PokemonId.Pidgeotto,
                //PokemonId.NidoranFemale,
                //PokemonId.Paras,
                //PokemonId.Venonat,
                //PokemonId.Psyduck,
                //PokemonId.Poliwag,
                //PokemonId.Slowpoke,
                //PokemonId.Drowzee,
                //PokemonId.Gastly,
                //PokemonId.Goldeen,
                //PokemonId.Staryu,
                //PokemonId.Dratini
                PokemonId.Magikarp,
                PokemonId.Eevee
            };

            var inventory = await client.GetInventory();
            var pokemons = inventory.InventoryDelta.InventoryItems
                                .Select(i => i.InventoryItemData?.Pokemon)
                                .Where(p => p != null && p?.PokemonId > 0)
                                .ToArray();

            //foreach (var unwantedPokemonType in unwantedPokemonTypes)
            {
                List<PokemonData> pokemonToDiscard;
                if (doNotTransfer.Count() != 0)
                    pokemonToDiscard = pokemons.Where(p => !doNotTransfer.Contains(p.PokemonId) && p.Cp < cpThreshold).OrderByDescending(p => p.Cp).ToList();
                else
                    pokemonToDiscard = pokemons.Where(p => p.Cp < cpThreshold).OrderByDescending(p => p.Cp).ToList();


                //var unwantedPokemon = pokemonOfDesiredType.Skip(1) // keep the strongest one for potential battle-evolving
                //                                          .ToList();
                bhelper.Main.ColoredConsoleWrite(ConsoleColor.Gray, $"[{DateTime.Now.ToString("HH:mm:ss")}] Grinding {pokemonToDiscard.Count} pokemon below {cpThreshold} CP.");
                await TransferAllGivenPokemons(client, pokemonToDiscard);

            }

            bhelper.Main.ColoredConsoleWrite(ConsoleColor.Gray, $"[{DateTime.Now.ToString("HH:mm:ss")}] Finished grinding all the meat");
        }

        public static async Task PrintLevel(Client client)
        {
            var inventory = await client.GetInventory();
            var stats = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.PlayerStats).ToArray();
            foreach (var v in stats)
                if (v != null)
                {
                    int XpDiff = bhelper.Game.GetXpDiff(v.Level);
                    if (_hero.ClientSettings.LevelOutput == "time")
                        bhelper.Main.ColoredConsoleWrite(ConsoleColor.Yellow,
                            $"[{DateTime.Now.ToString("HH:mm:ss")}] Current Level: " + v.Level + " (" +
                            (v.Experience - XpDiff) + "/" + (v.NextLevelXp - XpDiff) + ")");
                    else if (_hero.ClientSettings.LevelOutput == "levelup")
                        if (_hero.Currentlevel != v.Level)
                        {
                            _hero.Currentlevel = v.Level;
                            bhelper.Main.ColoredConsoleWrite(ConsoleColor.Magenta,
                                $"[{DateTime.Now.ToString("HH:mm:ss")}] Current Level: " + v.Level +
                                ". XP needed for next Level: " + (v.NextLevelXp - v.Experience));
                        }
                }
            if (_hero.ClientSettings.LevelOutput == "levelup")
                await Task.Delay(1000);
            else
                await Task.Delay(_hero.ClientSettings.LevelTimeInterval*1000);

            PrintLevel(client);
        }
        

        public static async Task ConsoleLevelTitle(string Username, Client client)
        {
            var inventory = await client.GetInventory();
            var stats = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.PlayerStats).ToArray();
            var profile = await client.GetProfile();
            foreach (var v in stats)
                if (v != null)
                {
                    int XpDiff = bhelper.Game.GetXpDiff(v.Level);
                    System.Console.Title = string.Format(Username + " | Level: {0:0} - ({1:0} / {2:0}) | Stardust: {3:0}", v.Level, (v.Experience - v.PrevLevelXp - XpDiff), (v.NextLevelXp - v.PrevLevelXp - XpDiff), profile.Profile.Currency.ToArray()[1].Amount) + " | XP/Hour: " + Math.Round(_hero.TotalExperience / bhelper.Main.GetRuntime(_hero.TimeStarted)) + " | Pokemon/Hour: " + Math.Round(_hero.TotalPokemon / bhelper.Main.GetRuntime(_hero.TimeStarted));
                }
            await Task.Delay(1000);
            ConsoleLevelTitle(Username, client);
        }
        
    }
}
