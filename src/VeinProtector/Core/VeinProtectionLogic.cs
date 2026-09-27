using System;
using System.Collections.Generic;

namespace VeinProtector.Core
{
    internal static class VeinProtectionLogic
    {
        internal const int KeepVeinAmount = 1;

        internal static bool IsSupportedProtectedResourceItem(int itemId)
        {
            if (itemId <= 0 || LDB.veins == null)
                return false;
            int maxVeinType = (int)EVeinType.Max;
            for (int veinTypeId = (int)EVeinType.None + 1; veinTypeId < maxVeinType; veinTypeId++)
            {
                if (veinTypeId == (int)EVeinType.Oil)
                    continue;
                VeinProto proto = LDB.veins.Select(veinTypeId);
                if (proto != null && IsSupportedProtectedVeinPrototype(itemId, veinTypeId, proto.MiningItem))
                    return true;
            }
            return false;
        }

        internal static bool IsSupportedProtectedVeinPrototype(int itemId, int veinTypeId, int miningItemId)
        {
            return itemId > 0 && miningItemId == itemId
                && veinTypeId > (int)EVeinType.None && veinTypeId < (int)EVeinType.Max
                && veinTypeId != (int)EVeinType.Oil;
        }

        internal static bool ShouldShowReferenceExtension(bool protectionEnabled, int itemId, bool supportedVeinItem)
        {
            return protectionEnabled && itemId > 0 && supportedVeinItem;
        }

        internal static double CalculateReferenceSpeedLoss(double baseReferenceSpeed, int physicalCount, int effectiveCount)
        {
            if (baseReferenceSpeed <= 0.0 || physicalCount <= effectiveCount)
                return 0.0;
            return baseReferenceSpeed * (physicalCount - effectiveCount);
        }

        internal static bool AddProtectedVeinId(HashSet<int> protectedVeinIds, int veinId)
        {
            return protectedVeinIds != null && veinId > 0 && protectedVeinIds.Add(veinId);
        }

        internal static float CalculateProtectedReferenceSpeed(bool protectionEnabled, float miningRate,
            float vanillaTotal, double protectionLoss)
        {
            if (!protectionEnabled || miningRate <= 0f || protectionLoss <= 0.0)
                return vanillaTotal;
            return Math.Max(0f, (float)(vanillaTotal - protectionLoss));
        }

        internal static IEnumerable<PlanetFactory> EnumerateFactoriesForAstroFilter(GameData data, int astroFilter)
        {
            if (data == null || data.factories == null || data.factoryCount <= 0)
                yield break;

            int factoryLimit = Math.Min(data.factoryCount, data.factories.Length);
            var yieldedFactoryIds = new HashSet<int>();

            if (astroFilter == -1)
            {
                for (int i = 0; i < factoryLimit; i++)
                {
                    PlanetFactory factory = data.factories[i];
                    if (IsActualFactory(factory, factoryLimit) && yieldedFactoryIds.Add(factory.index))
                        yield return factory;
                }
                yield break;
            }

            if (astroFilter == 0)
            {
                PlanetData localPlanet = data.localPlanet;
                if (localPlanet != null)
                {
                    PlanetFactory localFactory = localPlanet.factory;
                    if (IsActualFactory(localFactory, factoryLimit) && yieldedFactoryIds.Add(localFactory.index))
                        yield return localFactory;
                    yield break;
                }
            }

            if (astroFilter % 100 == 0)
            {
                StarData star = astroFilter == 0
                    ? data.localStar
                    : (data.galaxy == null ? null : data.galaxy.StarById(astroFilter / 100));
                if (star == null || star.planets == null)
                    yield break;

                int planetLimit = Math.Min(star.planetCount, star.planets.Length);
                for (int i = 0; i < planetLimit; i++)
                {
                    PlanetData planet = star.planets[i];
                    PlanetFactory factory = planet == null ? null : planet.factory;
                    if (IsActualFactory(factory, factoryLimit) && yieldedFactoryIds.Add(factory.index))
                        yield return factory;
                }
                yield break;
            }

            PlanetData selectedPlanet = data.galaxy == null ? null : data.galaxy.PlanetById(astroFilter);
            PlanetFactory selectedFactory = selectedPlanet == null ? null : selectedPlanet.factory;
            if (IsActualFactory(selectedFactory, factoryLimit) && yieldedFactoryIds.Add(selectedFactory.index))
                yield return selectedFactory;
        }

        private static bool IsActualFactory(PlanetFactory factory, int factoryLimit)
        {
            return factory != null && factory.index >= 0 && factory.index < factoryLimit;
        }

        internal static int GetEffectiveVeinCount(MinerComponent miner, VeinData[] veinPool, float miningRate)
        {
            if (!Plugin.IsProtectionEnabled || miner.type != EMinerType.Vein || miningRate <= 0f)
                return miner.veinCount;

            if (miner.minimumVeinAmount > KeepVeinAmount)
                return miner.veinCount;

            if (veinPool == null || miner.veins == null)
                return 0;

            int count = 0;
            int boundCount = Math.Min(miner.veinCount, miner.veins.Length);
            for (int i = 0; i < boundCount; i++)
            {
                int veinId = miner.veins[i];
                if (veinId > 0 && veinId < veinPool.Length
                    && veinPool[veinId].id != 0 && veinPool[veinId].amount > 1)
                    count++;
            }

            return count;
        }

        internal static int GetProductionTickCount(MinerComponent miner, VeinData[] veinPool, float miningRate)
        {
            int effectiveCount = GetEffectiveVeinCount(miner, veinPool, miningRate);
            if (effectiveCount > 0 || miner.type != EMinerType.Vein || miningRate <= 0f
                || veinPool == null || miner.veins == null)
                return effectiveCount;

            // Empty or invalid bindings must still reach vanilla's cleanup branch. These
            // administrative cycles cannot produce because amount<=0 stays on the vanilla path.
            int boundCount = Math.Min(miner.veinCount, miner.veins.Length);
            for (int i = 0; i < boundCount; i++)
            {
                int veinId = miner.veins[i];
                if (veinId <= 0 || veinId >= veinPool.Length || veinPool[veinId].id == 0
                    || veinPool[veinId].amount <= 0)
                    return miner.veinCount;
            }

            return 0;
        }

        internal static int GetEffectiveVeinCount(MinerComponent miner, VeinData[] veinPool, PlanetFactory factory)
        {
            float miningRate = 0f;
            if (factory != null && factory.gameData != null && factory.gameData.history != null)
                miningRate = factory.gameData.history.miningCostRate;

            return GetEffectiveVeinCount(miner, veinPool, miningRate);
        }

        internal static int GetEffectiveVeinCount(MinerComponent miner, PlanetFactory factory)
        {
            return GetEffectiveVeinCount(miner, factory == null ? null : factory.veinPool, factory);
        }

        internal static bool IsProtectedVein(MinerComponent miner, VeinData[] veinPool, float miningRate, int veinId)
        {
            bool isProtected = Plugin.IsProtectionEnabled
                && miner.type == EMinerType.Vein
                && miningRate > 0f
                && veinPool != null
                && veinId > 0
                && veinId < veinPool.Length
                && veinPool[veinId].id != 0
                && veinPool[veinId].amount == 1;
            return isProtected;
        }

        internal static int GetMaxAllowedCost(int amount, MinerComponent miner, float miningRate)
        {
            // This is only used by vanilla's capped-batch path. Keeping one unit out of its
            // available budget lets the original costFrac/times/product math remain intact.
            return Plugin.IsProtectionEnabled && miner.type == EMinerType.Vein && miningRate > 0f && amount > 1
                ? amount - 1
                : amount;
        }

        internal static void AdvanceCurrentVein(ref MinerComponent miner)
        {
            int count = miner.veinCount;
            if (count <= 0)
            {
                miner.currentVeinIndex = 0;
                return;
            }

            int next = miner.currentVeinIndex + 1;
            miner.currentVeinIndex = next >= count ? 0 : next;
        }

        internal static bool AreAllBoundVeinsProtected(MinerComponent miner, VeinData[] veinPool, float miningRate)
        {
            if (!Plugin.IsProtectionEnabled || miner.type != EMinerType.Vein || miningRate <= 0f
                || miner.veinCount <= 0 || veinPool == null || miner.veins == null)
                return false;

            int boundCount = Math.Min(miner.veinCount, miner.veins.Length);
            if (boundCount == 0)
                return false;

            for (int i = 0; i < boundCount; i++)
            {
                int veinId = miner.veins[i];
                // Invalid or exhausted veins must continue through vanilla cleanup.
                if (veinId <= 0 || veinId >= veinPool.Length
                    || veinPool[veinId].id == 0 || veinPool[veinId].amount != 1)
                    return false;
            }

            return boundCount == miner.veinCount;
        }
    }
}
