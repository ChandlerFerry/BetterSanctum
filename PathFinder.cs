using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore.PoEMemory.Elements.Sanctum;
using SharpDX;
using Vector2 = System.Numerics.Vector2;

namespace BetterSanctum;

public class PathFinder
{
    private readonly List<List<SanctumRoomElement>> roomsByLayer;
    private readonly int floorNumber = -1;
    private readonly byte[][][] roomLayout;
    public readonly int playerLayerIndex = -1;
    public readonly int playerRoomIndex = -1;
    private readonly bool randomAfflictionOnAffliction = false;
    private readonly bool ignoreMinorAfflictions = false;
    private readonly bool trapResolveAffliction = false;
    private static readonly string[] majorAfflictions = { "Anomaly Attractor", "Chiselled Stone", "Corrosive Concoction", "Cutpurse", "Deadly Snare", "Death Toll", "Demonic Skull", "Ghastly Scythe", "Glass Shard", "Orb of Negation", "Unassuming Brick", "Veiled Sight" };

    public readonly int currentResolve = 0;
    public readonly int inspiration = 0;
    public readonly int gold = 0;
    public readonly int maxResolve = 0;

    private readonly Dictionary<(int, int), int> roomWeightMap = new Dictionary<(int, int), int>();

    public static BetterSanctumPlugin Core;
    public PathFinder(BetterSanctumPlugin core)
    {
        Core = core;

        SanctumFloorWindow floorWindow = Core.GameController.IngameState.IngameUi.SanctumFloorWindow;

        playerLayerIndex = floorWindow.FloorData.RoomChoices.Count - 1;
        if (floorWindow.FloorData.RoomChoices.Count > 0)
        {
            playerRoomIndex = floorWindow.FloorData.RoomChoices.Last();
        }
        roomsByLayer = floorWindow.RoomsByLayer;
        roomLayout = floorWindow.FloorData.RoomLayout;
        floorNumber = CalculateFloorNumber(floorWindow);
        currentResolve = floorWindow.FloorData.CurrentResolve;
        inspiration = floorWindow.FloorData.Inspiration;
        gold = floorWindow.FloorData.Gold;
        maxResolve = floorWindow.FloorData.MaxResolve;

        /** Boons & Afflictions & Relics */
        var mapStats = Core.GameController.IngameState.Data.MapStats;
        randomAfflictionOnAffliction = mapStats.ContainsKey(ExileCore.Shared.Enums.GameStat.SanctumGainRandomMinorAfflictionOnGainingAffliction);
        ignoreMinorAfflictions = mapStats.ContainsKey(ExileCore.Shared.Enums.GameStat.SanctumPreventMinorAfflictions);
        trapResolveAffliction = mapStats.Any(keyValuePair => keyValuePair.Key.ToString() == "AfflictionTrapSanctumDamage");


        /*// TODO: Extract this out
        bool avoidFountain = mapStats.ContainsKey(ExileCore.Shared.Enums.GameStat.SanctumGainRandomMinorAfflictionOnFountainUse);
        bool reduceMerchant = mapStats.ContainsKey(ExileCore.Shared.Enums.GameStat.SanctumMerchantOnlyOneOption);

        // Significantly Harder (likely not worth programming)
        bool seekOneAffliction = mapStats.ContainsKey(ExileCore.Shared.Enums.GameStat.SanctumNextAfflictionConvertedToMinorBoon);*/
    }

    public List<(int, int)> FindBestPath()
    {
        int numLayers = roomLayout.Length;
        var startNode = (7, 0);

        var bestPath = new Dictionary<(int, int), List<(int, int)>> { { startNode, new List<(int, int)> { startNode } } };
        var minCost = new Dictionary<(int, int), int>();
        foreach (var room in roomWeightMap.Keys)
        {
            minCost[room] = int.MaxValue;
        }
        minCost[startNode] = roomWeightMap[startNode];

        var queue = new SortedSet<(int, int)>(Comparer<(int, int)>.Create((a, b) =>
        {
            int costA = minCost[a];
            int costB = minCost[b];
            if (costA != costB)
            {
                return costA.CompareTo(costB);
            }
            // If costs are equal, break the tie by comparing the nodes
            return a.CompareTo(b);
        }))
    {
        startNode
    };

        while (queue.Any())
        {
            var currentRoom = queue.First();
            queue.Remove(currentRoom); // Remove the processed node from the queue

            foreach (var neighbor in GetNeighbors(currentRoom, roomLayout))
            {
                int neighborCost = minCost[currentRoom] + roomWeightMap[neighbor];

                if (neighborCost < minCost[neighbor])
                {
                    // Remove the old entry before adding the updated one
                    queue.Remove(neighbor);

                    // Update the minimum cost and best path
                    minCost[neighbor] = neighborCost;

                    // Add the neighbor to the queue at the correct position
                    queue.Add(neighbor);

                    // Create a new list for the neighbor node and copy the path from the current node
                    bestPath[neighbor] = new List<(int, int)>(bestPath[currentRoom]) { neighbor };
                }
            }
        }

        // DEBUGGING
        /*foreach (var kvp in bestPath)
        {
            var key = kvp.Key;
            var value = kvp.Value;

            // Output the key and value to LogError
            LogError($"Key: {key}, Value: {string.Join(", ", value)}, minCost: {minCost[key]}");
        }*/

        var groupedPaths = bestPath.GroupBy(pair => pair.Value.Count());
        var maxCountGroup = groupedPaths.OrderByDescending(group => group.Key).FirstOrDefault();
        var path = maxCountGroup.OrderBy(pair => minCost.GetValueOrDefault(pair.Key, int.MaxValue)).FirstOrDefault().Value;
        if (playerLayerIndex != -1 && playerRoomIndex != -1)
        {
            path = bestPath.TryGetValue((playerLayerIndex, playerRoomIndex), out var specificPath) ? specificPath : new List<(int, int)>();
        }


        if (path == null)
        {
            return new List<(int, int)>();
        }

        return path;
    }

    private static IEnumerable<(int, int)> GetNeighbors((int, int) currentRoom, byte[][][] connections)
    {
        int currentLayerIndex = currentRoom.Item1;
        int currentRoomIndex = currentRoom.Item2;
        int previousLayerIndex = currentLayerIndex - 1;

        if (currentLayerIndex == 0)
        {
            yield break; // No neighbors to yield
        }

        byte[][] previousLayer = connections[previousLayerIndex];

        for (int previousLayerRoomIndex = 0; previousLayerRoomIndex < previousLayer.Length; previousLayerRoomIndex++)
        {
            var previousLayerRoom = previousLayer[previousLayerRoomIndex];

            if (previousLayerRoom.Contains((byte)currentRoomIndex))
            {
                yield return (previousLayerIndex, previousLayerRoomIndex);
            }
        }
    }

    public void CreateRoomWeightMap()
    {
        for (var layerIndex = roomsByLayer.Count - 1; layerIndex >= 0; layerIndex--)
        {
            var roomLayer = roomsByLayer[layerIndex];
            for (var roomIndex = 0; roomIndex < roomLayer.Count; roomIndex++)
            {
                var room = roomLayer[roomIndex];

                int numConnections = roomLayout[layerIndex][roomIndex].Length;

                roomWeightMap[(layerIndex, roomIndex)] = CalculateRoomWeight(room, layerIndex, numConnections);
            }
        }
    }

    private int CalculateRoomWeight(SanctumRoomElement room, int roomLayerIndex, int numConnections)
    {
        int roomWeight = 1000000; // Base weight

        string floorSuffix = $"_Floor{floorNumber}";

        string debugText = "";
        string playerText = "";

        // FightRoom Weight
        var fightRoomId = room.Data.FightRoom?.RoomType?.Id;
        if (fightRoomId != null)
        {
            int fightRoomWeight = Core.Settings.GetFightRoomWeight(fightRoomId + floorSuffix);
            if (fightRoomId == "Arena" && trapResolveAffliction)
            {
                fightRoomWeight *= 4;
            } else if (fightRoomId == "Explore" && (currentResolve + inspiration) < 50)
            {
                roomWeight *= 10;
            }
            playerText += $"{fightRoomId}";
            debugText += $"\nRoomType: {fightRoomWeight}";
            roomWeight -= fightRoomWeight;
        }

        // Affliction Weight
        var roomAffliction = room.Data?.RoomEffect?.ReadableName;
        if (roomAffliction != null)
        {
            int afflictionWeight = Core.Settings.GetAfflictionWeight(roomAffliction + floorSuffix);
            if (randomAfflictionOnAffliction)
            {
                afflictionWeight -= -100; // TODO: Weighted average towards the top of the spectrum
            }
            if (ignoreMinorAfflictions && !majorAfflictions.Any(roomAffliction.Equals))
            {
                afflictionWeight = 0;
            }
            if (floorNumber == 4)
            {
                if (roomAffliction.Equals("Floor Tax"))
                {
                    afflictionWeight = 0;
                }
            }
            debugText += $"\nAffliction: {afflictionWeight}";
            roomWeight -= afflictionWeight;
        }

        // Reward Weight
        var rewardOne = room.Data?.Reward1?.CurrencyName;
        var rewardTwo = room.Data?.Reward2?.CurrencyName;
        var rewardThree = room.Data?.Reward3?.CurrencyName;
        if (rewardOne != null && rewardOne != null && rewardThree != null)
        {
            int rewardWeight1 = Core.Settings.GetCurrencyWeight(rewardOne + "_Now");
            int rewardWeight2 = Core.Settings.GetCurrencyWeight(rewardTwo + "_EndOfFloor");
            int rewardWeight3 = Core.Settings.GetCurrencyWeight(rewardThree + "_EndOfSanctum");
            int maxRewardWeight = Math.Max(Math.Max(rewardWeight1, rewardWeight2), rewardWeight3);
            if(rewardWeight1 > 5000)
            {
                playerText += $"\n{rewardOne}";
            }
            if (rewardWeight2 > 5000)
            {
                playerText += $"\n{rewardTwo}";
            }
            if (rewardWeight3 > 5000)
            {
                playerText += $"\n{rewardThree}";
            }
            debugText += $"\nCurrency: {maxRewardWeight}";
            roomWeight -= maxRewardWeight;
        }
        // Room Weight
        var roomType = room.Data?.RewardRoom?.RoomType?.Id;
        if (roomType != null)
        {
            int roomTypeWeight = Core.Settings.GetRoomTypeWeight(roomType + floorSuffix);
            debugText += $"\nRewardType: {roomTypeWeight}";
            if (roomType != "Deferral")
            {
                roomWeight -= roomTypeWeight;
            }
            else if (rewardOne == null && rewardTwo == null && rewardThree == null)
            {
                roomWeight -= roomTypeWeight;
            }
        }


        // If total is still zero give it a base value of 25 on floor one and two, 75 on floor three and four
        if (debugText == "")
        {
            if (floorNumber == 1 || floorNumber == 2)
            {
                roomWeight -= 25; // empty value floor one two
            }
            else
            {
                roomWeight -= 75; // empty value floor three four
            }
        }

        // Paths Weight
        if (numConnections > 0)
        {
            int connectionsWeight = numConnections * 5;
            debugText += $"\nConnections: {connectionsWeight}";
            roomWeight -= connectionsWeight;
        }

        debugText += $"\nTotal: {1000000 - roomWeight}";

        if (Core.Settings.DebugEnable)
        {
            Vector2 weightTextPosition = new Vector2(room.GetClientRectCache.TopLeft.X, room.GetClientRectCache.TopLeft.Y);
            DrawTextWithBackground(playerText + debugText, weightTextPosition, Core.Settings.TextColor, Core.Settings.BackgroundColor); // You can customize the color
        } else
        {
            Vector2 weightTextPosition = new Vector2(room.GetClientRectCache.TopLeft.X, room.GetClientRectCache.TopLeft.Y);
            DrawTextWithBackground(playerText, weightTextPosition, Core.Settings.TextColor, Core.Settings.BackgroundColor); // You can customize the color
        }

        return roomWeight;
    }

    private static int CalculateFloorNumber(SanctumFloorWindow floorWindow)
    {
        string bossId = floorWindow?.Rooms?.Last()?.Data?.FightRoom?.Id;

        if (bossId == "Cellar_Boss_1_1")
        {
            return 1;
        }
        else if (bossId == "Vaults_Boss_1_1")
        {
            return 2;
        }
        else if (bossId == "Nave_Boss_1_1")
        {
            return 3;
        }
        else if (bossId == "Crypt_Boss_1_1")
        {
            return 4;
        }

        return -1;
    }

    private static Vector2 DrawTextWithBackground(string text, Vector2 position, Color color, Color backgroundColor)
    {
        var textSize = Core.Graphics.MeasureText(text);
        Core.Graphics.DrawBox(position, textSize + position, backgroundColor);
        Core.Graphics.DrawText(text, position, color);
        return textSize;
    }
}
