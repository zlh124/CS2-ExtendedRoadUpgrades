// <copyright file="Setup.cs" company="ST-Apps (S. Tenuta)">
// Copyright (c) ST-Apps (S. Tenuta). All rights reserved.
// Licensed under the Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International License (the "License"); you may not use this file except in compliance with the License.
// See LICENSE.txt file in the project root for full license information.
// </copyright>

namespace ExtendedRoadUpgrades.Code
{
    using System.Collections.Generic;
    using System.Linq;
    using Colossal.Json;
    using Game.Prefabs;
    using Game.SceneFlow;
    using HarmonyLib;
    using Unity.Entities;
    using UnityEngine;

    /// <summary>
    /// This class provides the utility methods to install the mod.
    /// </summary>
    internal static class UpgradesManager
    {
        /// <summary>
        ///     Name of the vanilla upgrade prefab we clone for every custom upgrade mode.
        /// </summary>
        private const string TemplatePrefabName = "Grass";

        /// <summary>
        ///     Base URI for all of our icons.
        /// </summary>
        private static readonly string COUIBaseLocation = $"coui://{Mod.ModIconsId}";

        /// <summary>
        ///     Guard boolean used to check if the first installation phase already executed, so that we can prevent executing it multiple times.
        /// </summary>
        private static bool installed;

        /// <summary>
        ///     Guard boolean used to check if the second installation phase already executed, so that we can prevent executing it multiple times.
        /// </summary>
        private static bool postInstalled;

        /// <summary>
        ///     Guard boolean used to check if we already subscribed to the game's loading events.
        /// </summary>
        private static bool subscribed;

        /// <summary>
        ///     Guard boolean set when one of our upgrade prefabs is already registered by something else (usually
        ///     the original, discontinued version of this mod still being enabled). In that case we must not touch
        ///     the existing prefabs, otherwise we would overwrite the other mod's upgrade data.
        /// </summary>
        private static bool conflictingPrefabDetected;

        /// <summary>
        ///     <see cref="world"/> instance used by our patch and by the loading event handler.
        /// </summary>
        private static World world;

        /// <summary>
        ///     <see cref="prefabSystem"/> instance used by our patch and by the loading event handler.
        /// </summary>
        private static PrefabSystem prefabSystem;

        /// <summary>
        ///     <para>
        ///         Installing the mode means to add our cloned <see cref="PrefabBase"/> to the global collection in
        ///         <see cref="prefabSystem"/>.
        ///     </para>
        ///     <para>
        ///         To avoid getting the wrong <see cref="world"/> instance we rely on Harmony's <see cref="Traverse"/> to extract the
        ///         <b>m_World</b> field from the injected <see cref="GameManager"/> instance.
        ///     </para>
        ///     <para>
        ///         After that, we leverage <see cref="World.GetOrCreateSystemManaged{T}"/> to get our target <see cref="prefabSystem"/>.
        ///         From there, to get <see cref="prefabSystem"/>'s internal <see cref="PrefabBase"/> list we use <see cref="Traverse"/>
        ///         again and we extract the <b>m_Prefabs</b> field.
        ///     </para>
        ///     <para>
        ///         We now have what it takes to extract our <see cref="PrefabBase"/> object, and as reference we extract the one called
        ///         <b>Grass</b>.
        ///         During this stage we only care for <see cref="ComponentBase"/> and not <see cref="IComponentData"/>.
        ///     </para>
        ///     <para>
        ///         The only <see cref="ComponentBase"/> we need to deal with is the attached <see cref="UIObject"/>, which contains the
        ///         <see cref="UIObject.m_Icon"/> property. This property is a relative URI pointing to a SVG file in your
        ///         <b>Cities2_Data\StreamingAssets\~UI~\GameUI\Media\Game\Icons</b> directory.
        ///     </para>
        ///     <para>
        ///         The <b>Cities2_Data\StreamingAssets\~UI~\GameUI\</b> MUST be omitted from the URI, resulting in a definition similar to:
        ///         <code>
        ///             myUIObject.m_Icon = "Media\Game\Icons\myIcon.svg
        ///         </code>
        ///     </para>
        ///     <para>
        ///         Once the <see cref="UIObject"/> is properly set with an updated <see cref="UIObject.m_Icon"/> and <see cref="UIObject.name"/>
        ///     </para>
        /// </summary>
        internal static void Install()
        {
            var logHeader = $"[{nameof(UpgradesManager)}.{nameof(Install)}]";

            if (installed)
            {
                Mod.Log.Info($"{logHeader} Mod is installed, skipping");
                return;
            }

            // Always listen to the game loading events: prefabs might not be available yet when mods are loaded,
            // and we need a second phase once the game (and its prefabs) are ready anyway.
            SubscribeToGameEvents();

            if (!InstallUpgradePrefabs())
            {
                Mod.Log.Warn($"{logHeader} Installation deferred, it will be retried when the game finishes loading.");
            }
        }

        /// <summary>
        ///     Subscribes (once) to the game's loading events.
        /// </summary>
        private static void SubscribeToGameEvents()
        {
            if (subscribed)
            {
                return;
            }

            GameManager.instance.onGameLoadingComplete += GameManager_onGameLoadingComplete;
            subscribed = true;

            Mod.Log.Info($"[{nameof(UpgradesManager)}] Ready to listen to GameManager loading events.");
        }

        /// <summary>
        ///     Resolves the <see cref="World"/> and <see cref="PrefabSystem"/> instances we work with.
        /// </summary>
        /// <returns><c>true</c> if both instances are available.</returns>
        private static bool TryGetSystems()
        {
            var logHeader = $"[{nameof(UpgradesManager)}.{nameof(TryGetSystems)}]";

            if (world != null && world.IsCreated && prefabSystem != null)
            {
                return true;
            }

            // Getting World instance
            world = Traverse.Create(GameManager.instance).Field<World>("m_World").Value;
            if (world == null)
            {
                Mod.Log.Error($"{logHeader} Failed retrieving World instance.");
                return false;
            }

            // Getting PrefabSystem instance from World
            prefabSystem = world.GetExistingSystemManaged<PrefabSystem>();
            if (prefabSystem == null)
            {
                Mod.Log.Error($"{logHeader} Failed retrieving PrefabSystem instance.");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Gets the list of prefabs currently loaded by the game.
        /// </summary>
        /// <returns>The prefab list, or <c>null</c> if it isn't available yet.</returns>
        private static List<PrefabBase> GetPrefabs()
        {
            var logHeader = $"[{nameof(UpgradesManager)}.{nameof(GetPrefabs)}]";

            if (!TryGetSystems())
            {
                return null;
            }

            var prefabs = Traverse.Create(prefabSystem).Field<List<PrefabBase>>("m_Prefabs").Value;
            if (prefabs == null || !prefabs.Any())
            {
                Mod.Log.Warn($"{logHeader} Prefabs list is not available yet.");
                return null;
            }

            return prefabs;
        }

        /// <summary>
        ///     Looks for the vanilla upgrade prefab used as a template for our custom upgrade modes.
        ///     The game renamed some of its prefabs over time, so if <b>Grass</b> isn't available we fall back to
        ///     any other prefab that looks like a net upgrade (it carries a <see cref="UIObject"/> and upgrade flags).
        /// </summary>
        /// <param name="prefabs">Loaded prefabs.</param>
        /// <returns>The template prefab, or <c>null</c> if none could be found.</returns>
        private static PrefabBase FindTemplatePrefab(List<PrefabBase> prefabs)
        {
            var logHeader = $"[{nameof(UpgradesManager)}.{nameof(FindTemplatePrefab)}]";

            var template = prefabs.FirstOrDefault(p => p.name == TemplatePrefabName);
            if (template != null)
            {
                return template;
            }

            Mod.Log.Warn($"{logHeader} Prefab '{TemplatePrefabName}' not found, looking for another upgrade prefab.");

            var candidates = prefabs
                .Where(p => p != null && p.TryGet<UIObject>(out var uiObject) && uiObject != null)
                .Where(p => prefabSystem.TryGetComponentData<PlaceableNetData>(p, out var data) && !data.Equals(default(PlaceableNetData)))
                .ToList();

            if (!candidates.Any())
            {
                Mod.Log.Error($"{logHeader} No upgrade prefab found. Loaded prefabs: {prefabs.Count}.");
                return null;
            }

            // Prefer prefabs that actually set upgrade flags, they are the closest match to what we need.
            template = candidates.FirstOrDefault(p =>
                prefabSystem.TryGetComponentData<PlaceableNetData>(p, out var data) &&
                !data.m_SetUpgradeFlags.Equals(default(CompositionFlags))) ?? candidates.First();

            Mod.Log.Warn($"{logHeader} Falling back to prefab '{template.name}' (candidates: {string.Join(", ", candidates.Select(p => p.name))}).");

            return template;
        }

        /// <summary>
        ///     First installation phase: clones the template upgrade prefab once per supported upgrade mode and
        ///     registers the copies into the <see cref="PrefabSystem"/>.
        /// </summary>
        /// <returns><c>true</c> when the clones have been registered.</returns>
        private static bool InstallUpgradePrefabs()
        {
            var logHeader = $"[{nameof(UpgradesManager)}.{nameof(InstallUpgradePrefabs)}]";

            if (installed)
            {
                Mod.Log.Info($"{logHeader} Mod is installed, skipping");
                return true;
            }

            Mod.Log.Info($"{logHeader} Installing mod");

            var prefabs = GetPrefabs();
            if (prefabs == null)
            {
                Mod.Log.Warn($"{logHeader} Prefabs are not available yet, exiting.");
                return false;
            }

            // Getting the original upgrade Prefab
            var templatePrefab = FindTemplatePrefab(prefabs);
            if (templatePrefab == null)
            {
                Mod.Log.Error($"{logHeader} Failed retrieving the original upgrade Prefab instance, exiting.");
                return false;
            }

            Mod.Log.Debug($"{logHeader} Retrieved the original '{templatePrefab.name}' Prefab instance.");

            // Getting the original upgrade Prefab's UIObject
            if (!templatePrefab.TryGet<UIObject>(out var templateUIObject) || templateUIObject == null)
            {
                Mod.Log.Error($"{logHeader} Failed retrieving the original Prefab's UIObject instance, exiting.");
                return false;
            }

            Mod.Log.Debug($"{logHeader} Retrieved the original Prefab's UIObject instance.");

            // We now have all the needed original objects to build our clones
            foreach (var upgradeMode in Data.ExtendedRoadUpgrades.Modes)
            {
                if (prefabs.Any(p => p.name == upgradeMode.Id))
                {
                    conflictingPrefabDetected = true;
                    Mod.Log.Error(
                        $"{logHeader} [{upgradeMode.Id}] A prefab with the same name is already registered. " +
                        "Another version of this mod is probably enabled: disable it (the original 'Extended Road Upgrades' " +
                        "and this mod cannot be used at the same time), then restart the game.");
                    continue;
                }

                // Use the game's own prefab cloning routine: it creates the prefab copy and, unlike
                // GameObject.Instantiate, it deep-copies every ComponentBase so that each component is owned
                // by the clone. Sharing components with the template prefab makes PrefabBase.OnEnable log
                // "Component on prefab ... is referenced from another prefab: ..." and re-parent the original
                // prefab's components to the clone, corrupting the vanilla upgrade.
                var clonedPrefab = templatePrefab.Clone(upgradeMode.Id);
                if (clonedPrefab == null)
                {
                    Mod.Log.Error($"{logHeader} [{upgradeMode.Id}] Failed cloning the original Prefab, exiting.");
                    return false;
                }

                Mod.Log.Debug($"{logHeader} [{upgradeMode.Id}] Cloned the original Prefab instance.");

                // Keep the prefab asset reference of the template prefab. Until 1.5 the clone inherited it
                // (GameObject.Instantiate copies the field) and some prefab queries rely on it.
                clonedPrefab.asset = templatePrefab.asset;

                // Update the UI component. Values are re-applied explicitly on top of the cloned component so
                // that references (like the UI group) are guaranteed to be the same as the template ones.
                if (!clonedPrefab.TryGet<UIObject>(out var clonedUIObject) || clonedUIObject == null)
                {
                    Mod.Log.Error($"{logHeader} [{upgradeMode.Id}] Failed retrieving the cloned Prefab's UIObject instance, exiting.");
                    return false;
                }

                clonedUIObject.m_Icon = $"{COUIBaseLocation}/{upgradeMode.Id}.svg";

                // Give the UI element a unique name: replace the template name when it is part of it,
                // otherwise fall back to the upgrade mode id so that instances never share a name.
                var clonedUIObjectName = templateUIObject.name?.Replace(TemplatePrefabName, upgradeMode.Id);
                clonedUIObject.name = string.IsNullOrWhiteSpace(clonedUIObjectName) || clonedUIObjectName == templateUIObject.name
                    ? upgradeMode.Id
                    : clonedUIObjectName;

                clonedUIObject.m_IsDebugObject = templateUIObject.m_IsDebugObject;
                clonedUIObject.m_Priority = templateUIObject.m_Priority;
                clonedUIObject.m_Group = templateUIObject.m_Group;
                clonedUIObject.active = templateUIObject.active;

                Mod.Log.Debug($"{logHeader} [{upgradeMode.Id}] Updated the cloned UIObject with name {clonedUIObject.name} and icon {clonedUIObject.m_Icon}.");

                // The template upgrade carries a NetUpgrade component that describes which net piece requirements
                // the upgrade sets and unsets. Those are merged into the prefab's PlaceableNetData flags by
                // NetInitializeSystem, so they are intentionally left untouched here: the mode behaviour is
                // driven by the PlaceableNetData flags written during the second installation phase.
                if (clonedPrefab.TryGet<NetUpgrade>(out var netUpgrade) && netUpgrade != null)
                {
                    Mod.Log.Debug(
                        $"{logHeader} [{upgradeMode.Id}] Inherited NetUpgrade state (standalone: {netUpgrade.m_Standalone}, underground: {netUpgrade.m_Underground}, " +
                        $"set: [{string.Join(", ", (netUpgrade.m_SetState ?? new NetPieceRequirements[0]).Select(r => r.ToString()))}], " +
                        $"unset: [{string.Join(", ", (netUpgrade.m_UnsetState ?? new NetPieceRequirements[0]).Select(r => r.ToString()))}]).");
                }

                // Since game version 1.5 AddPrefab requires the parent prefab / component (used to log failures),
                // the actual behaviour is unchanged when passing null for all of them (our prefabs are root prefabs).
                if (!prefabSystem.AddPrefab(clonedPrefab, null, null, null))
                {
                    Mod.Log.Error($"{logHeader} [{upgradeMode.Id}] Failed adding the cloned Prefab to PrefabSystem, exiting.");
                    return false;
                }

                LogPrefabState(logHeader, upgradeMode.Id, clonedPrefab);

                Mod.Log.Info($"{logHeader} [{upgradeMode.Id}] Successfully created and added our cloned Prefab to PrefabSystem.");
            }

            // Mark the first phase as already executed
            installed = true;

            Mod.Log.Info($"{logHeader} Completed.");

            return true;
        }

        /// <summary>
        ///     Logs the state of the entity the game created for the given prefab. Useful to diagnose upgrades that
        ///     are registered but never show up in the upgrade menu: <see cref="Unlock"/> is required for a prefab to
        ///     be listed, while <see cref="Locked"/> means the prefab is still waiting to be unlocked.
        /// </summary>
        /// <param name="logHeader">Log prefix.</param>
        /// <param name="prefabName">Prefab name.</param>
        /// <param name="prefab">Prefab instance.</param>
        private static void LogPrefabState(string logHeader, string prefabName, PrefabBase prefab)
        {
            try
            {
                var prefabEntity = prefabSystem.GetEntity(prefab);
                var entityManager = world.EntityManager;
                var components = new List<string>();

                if (entityManager.HasComponent<Unlock>(prefabEntity))
                {
                    components.Add(nameof(Unlock));
                }

                if (entityManager.HasComponent<Locked>(prefabEntity))
                {
                    components.Add(nameof(Locked));
                }

                if (entityManager.HasComponent<UnlockRequirement>(prefabEntity))
                {
                    components.Add(nameof(UnlockRequirement));
                }

                if (entityManager.HasComponent<PlaceableNetData>(prefabEntity))
                {
                    components.Add(nameof(PlaceableNetData));
                }

                Mod.Log.Info(
                    $"{logHeader} [{prefabName}] Prefab entity {prefabEntity.Index}:{prefabEntity.Version} has [{string.Join(", ", components)}], " +
                    $"asset: {prefab.asset?.name ?? "<none>"}, builtin: {prefab.isBuiltin}.");
            }
            catch (System.Exception e)
            {
                Mod.Log.Warn($"{logHeader} [{prefabName}] Failed inspecting the prefab entity: {e.Message}");
            }
        }

        /// <summary>
        ///     <para>
        ///         This event handler performs the second phase of our custom modes patching.
        ///     </para>
        ///     <para>
        ///         While in the first phase we create the <see cref="PrefabBase"/> without any <see cref="IComponentData"/>, in this one
        ///         we add the <see cref="IComponentData"/> that we need to define the behavior of our custom upgrade modes.
        ///     </para>
        ///     <para>
        ///         This behavior is defined by the <see cref="PlaceableNetData"/> <see cref="IComponentData"/>, which allows us to specify
        ///         a collection of <see cref="PlaceableNetData.m_SetUpgradeFlags"/> and <see cref="PlaceableNetData.m_UnsetUpgradeFlags"/>.
        ///     </para>
        ///     <para>
        ///         These two collection contains all the needed <see cref="CompositionFlags"/> that game will use to compose the final road.
        ///     </para>
        ///     <para>
        ///         <list type="bullet">
        ///             <ul>
        ///                 <see cref="PlaceableNetData.m_SetUpgradeFlags"/> contains the flags that must be added to the target road piece
        ///             </ul>
        ///             <ul>
        ///                 <see cref="PlaceableNetData.m_UnsetUpgradeFlags"/> contains the flags that must be removed from the target road piece
        ///             </ul>
        ///         </list>
        ///     </para>
        ///     <para>
        ///         The goal of this method is then to simply iterate over our cloned <see cref="PrefabBase"/> Prefabs and add to each one of them
        ///         the appropriate <see cref="PlaceableNetData"/>, based on the data set in our <see cref="Data.ExtendedRoadUpgrades.Modes"/> collection.
        ///     </para>
        /// </summary>
        /// <param name="purpose">Loading purpose.</param>
        /// <param name="mode">Game mode being loaded.</param>
        private static void GameManager_onGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, Game.GameMode mode)
        {
            var logHeader = $"[{nameof(UpgradesManager)}.{nameof(GameManager_onGameLoadingComplete)}]";

            if (postInstalled)
            {
                Mod.Log.Info($"{logHeader} Already executed before, skipping.");
                return;
            }

            // Execute in Game mode only
            if (mode != Game.GameMode.Game && mode != Game.GameMode.Editor)
            {
                Mod.Log.Info($"{logHeader} Game mode is {mode}, skipping.");
                return;
            }

            if (!installed && !InstallUpgradePrefabs())
            {
                Mod.Log.Error($"{logHeader} Upgrade prefabs could not be installed, exiting.");
                return;
            }

            // Never overwrite upgrade prefabs owned by somebody else (usually the original version of this mod).
            if (conflictingPrefabDetected)
            {
                Mod.Log.Error(
                    $"{logHeader} Upgrade prefabs already exist and were not created by this mod; refusing to modify them. " +
                    "Disable the other version of this mod and restart the game.");
                return;
            }

            Mod.Log.Info($"{logHeader} Started.");

            // Getting Prefabs list from PrefabSystem
            var prefabs = GetPrefabs();
            if (prefabs == null)
            {
                Mod.Log.Error($"{logHeader} Failed retrieving Prefabs list, exiting.");
                return;
            }

            // We now have all the needed objects to patch our clones
            foreach (var upgradeMode in Data.ExtendedRoadUpgrades.Modes)
            {
                // Getting the cloned Prefab for the current upgrade mode
                var clonedPrefab = prefabs.FirstOrDefault(p => p.name == upgradeMode.Id);
                if (clonedPrefab == null)
                {
                    Mod.Log.Error($"{logHeader} [{upgradeMode.Id}] Failed retrieving the cloned Prefab instance, exiting.");
                    return;
                }

                Mod.Log.Debug($"{logHeader} [{upgradeMode.Id}] Retrieved the cloned Prefab instance.");

                // Getting the cloned Prefab's PlaceableNetData for the current upgrade mode
                if (!prefabSystem.TryGetComponentData<PlaceableNetData>(clonedPrefab, out var clonedPrefabData))
                {
                    Mod.Log.Error($"{logHeader} [{upgradeMode.Id}] Failed retrieving the cloned Prefab's PlaceableNetData instance, exiting.");
                    return;
                }

                Mod.Log.Debug($"{logHeader} [{upgradeMode.Id}] Retrieved the cloned Prefab's PlaceableNetData instance.");

                // Update the flags with the ones set in our upgrade mode
                clonedPrefabData.m_SetUpgradeFlags = upgradeMode.m_SetUpgradeFlags;

                // TODO: this works even without the unset flags, keeping them there just in case
                clonedPrefabData.m_UnsetUpgradeFlags = upgradeMode.m_UnsetUpgradeFlags;

                // This toggles underground mode for our custom upgrade modes
                if (upgradeMode.IsUnderground)
                {
                    clonedPrefabData.m_PlacementFlags |= Game.Net.PlacementFlags.UndergroundUpgrade;
                }

                // Persist the updated flags by replacing the ComponentData with the one we just created
                prefabSystem.AddComponentData(clonedPrefab, clonedPrefabData);

                Mod.Log.Info($"{logHeader} [{upgradeMode.Id}] Successfully set flags for our cloned Prefab to {clonedPrefabData.m_SetUpgradeFlags.ToJSONString()} and {clonedPrefabData.m_UnsetUpgradeFlags.ToJSONString()}.");
            }

            // Mark the second phase as already executed
            postInstalled = true;

            Mod.Log.Info($"{logHeader} Completed.");
        }
    }
}
