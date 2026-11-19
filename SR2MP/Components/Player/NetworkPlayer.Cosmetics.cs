using Il2CppInterop.Runtime.Attributes;
using Il2CppMonomiPark.SlimeRancher.Player.PlayerItems;
using Il2CppMonomiPark.SlimeRancher.Ranch;
using SR2MP.Shared.Managers;

namespace SR2MP.Components.Player;

// What another player's avatar shows of them: their own vac palette, their vac stream, and
// whether it is shown at all (not while they are in another scene).
internal partial class NetworkPlayer
{
    // The local player's own vac (local avatar only), read for the vac mode it sends.
    private VacuumItem? localVacuumItem;

    #region Vac palette

    private const float CosmeticsRetrySeconds = 0.5f;

    // The palette id last put on this avatar; null until the first one.
    private string? appliedVacPalette;
    private float nextCosmeticsTry;
    private bool vacPaletteSetupDone;
    private bool vacPaletteLogged;
    private bool vacPaletteFailed;

    // Path A: the avatar has the game's own palette components for the vac.
    private List<ApplyPalette>? vacApplyPalettes;
    private VacPaletteApplicator? vacApplicator;

    // Path B: no such components; the renderer material slots that use a vac palette material.
    private readonly struct VacMaterialUse
    {
        public readonly Renderer Renderer;
        public readonly int Index;
        public readonly IntPtr Slot;

        public VacMaterialUse(Renderer renderer, int index, IntPtr slot)
        {
            Renderer = renderer;
            Index = index;
            Slot = slot;
        }
    }

    private List<VacMaterialUse>? vacMaterialUses;

    private void UpdateCosmetics()
    {
        if (vacPaletteFailed || !PlayerCosmeticsManager.TryGet(ID, out var wanted) || wanted == appliedVacPalette)
            return;

        var now = UnityEngine.Time.unscaledTime;
        if (now < nextCosmeticsTry)
            return;
        nextCosmeticsTry = now + CosmeticsRetrySeconds;

        if (!PlayerCosmeticsManager.DirectorReady)
            return;

        try
        {
            var palette = PlayerCosmeticsManager.FindVacPalette(wanted);
            if (palette == null)
                return;

            ApplyVacPalette(palette);
            appliedVacPalette = wanted;
        }
        catch (Exception ex)
        {
            // Only the look is lost; the avatar keeps working.
            vacPaletteFailed = true;
            SrLogger.LogWarning($"Could not put vac palette {wanted} on avatar {ID}: {ex.Message}");
        }
    }

    private void ApplyVacPalette(VacPalette palette)
    {
        if (!vacPaletteSetupDone)
            SetupVacPaletteTargets();

        var renderersChanged = 0;
        var path = "none";

        if (vacApplyPalettes is { Count: > 0 })
        {
            path = "A";
            foreach (var applyPalette in vacApplyPalettes)
            {
                if (!applyPalette)
                    continue;

                try
                {
                    vacApplicator ??= new VacPaletteApplicator();
                    vacApplicator.Apply(applyPalette, palette);
                }
                catch (Exception ex)
                {
                    path = "A (by hand)";
                    SrLogger.LogDebug($"The game's vac palette applicator failed on avatar {ID}, applying by hand: {ex.Message}");
                    vacApplicator = null;
                    ApplyVacPaletteByHand(applyPalette, palette);
                }

                var assignments = applyPalette.PaletteSlotAssignments;
                if (assignments == null)
                    continue;

                foreach (var assignment in assignments)
                    renderersChanged += assignment?.renderers?.Length ?? 0;
            }
        }
        else if (vacMaterialUses is { Count: > 0 })
        {
            path = "B";
            renderersChanged = ApplyVacPaletteToMaterials(palette);
        }

        if (vacPaletteLogged)
            return;

        vacPaletteLogged = true;
        SrLogger.LogMessage(
            $"vac palette on avatar {ID}: path {path}, {vacApplyPalettes?.Count ?? 0} palette components, " +
            $"{vacMaterialUses?.Count ?? 0} material slots, {renderersChanged} renderers");

        if (path == "none")
            LogAvatarMaterials();
    }

    private void SetupVacPaletteTargets()
    {
        vacPaletteSetupDone = true;

        vacApplyPalettes = new List<ApplyPalette>();
        foreach (var applyPalette in GetComponentsInChildren<ApplyPalette>(true))
        {
            if (!applyPalette)
                continue;

            var target = applyPalette.Target;
            if (target && target.TryCast<VacPaletteTarget>() != null)
                vacApplyPalettes.Add(applyPalette);
        }

        if (vacApplyPalettes.Count > 0)
            return;

        // Which material belongs to which palette slot, from every vac palette the game has.
        var slotByMaterialName = new Dictionary<string, IntPtr>();
        foreach (var vacPalette in PlayerCosmeticsManager.GetAllVacPalettes())
        {
            var slots = vacPalette._materialSlots;
            if (slots == null)
                continue;

            foreach (var slot in slots)
            {
                if (slot == null || !slot.Material || !slot.Slot)
                    continue;

                slotByMaterialName.TryAdd(CleanMaterialName(slot.Material.name), slot.Slot.Pointer);
            }
        }

        vacMaterialUses = new List<VacMaterialUse>();
        if (slotByMaterialName.Count == 0)
            return;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer || (UsernamePanel && renderer.gameObject == UsernamePanel.gameObject))
                continue;

            var materials = renderer.sharedMaterials;
            if (materials == null)
                continue;

            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material && slotByMaterialName.TryGetValue(CleanMaterialName(material.name), out var slot))
                    vacMaterialUses.Add(new VacMaterialUse(renderer, i, slot));
            }
        }
    }

    // Shared materials only: the palette's own materials are put in place, so nothing is copied.
    private int ApplyVacPaletteToMaterials(VacPalette palette)
    {
        var changedRenderers = 0;

        foreach (var group in vacMaterialUses!.GroupBy(use => use.Renderer.Pointer))
        {
            var renderer = group.First().Renderer;
            if (!renderer)
                continue;

            var materials = renderer.sharedMaterials;
            var changed = false;

            foreach (var use in group)
            {
                var material = FindSlotMaterial(palette, use.Slot);
                if (!material || use.Index >= materials.Length)
                    continue;

                materials[use.Index] = material;
                changed = true;
            }

            if (!changed)
                continue;

            renderer.sharedMaterials = materials;
            changedRenderers++;
        }

        return changedRenderers;
    }

    private static void ApplyVacPaletteByHand(ApplyPalette applyPalette, VacPalette palette)
    {
        var slots = palette._materialSlots;
        if (slots == null)
            return;

        var vacMaterialNames = new HashSet<string>();
        foreach (var slot in slots)
        {
            if (slot != null && slot.Material)
                vacMaterialNames.Add(CleanMaterialName(slot.Material.name));
        }

        var assignments = applyPalette.PaletteSlotAssignments;
        if (assignments == null)
            return;

        foreach (var assignment in assignments)
        {
            if (assignment == null || !assignment.paletteSlotIdentifier)
                continue;

            VacPaletteSlotMaterial? match = null;
            foreach (var slot in slots)
            {
                if (slot != null && slot.Slot && slot.Slot.Pointer == assignment.paletteSlotIdentifier.Pointer)
                {
                    match = slot;
                    break;
                }
            }

            if (match == null)
                continue;

            if (match.Material && assignment.renderers != null)
            {
                foreach (var renderer in assignment.renderers)
                {
                    if (!renderer)
                        continue;

                    var materials = renderer.sharedMaterials;
                    if (materials.Length == 1)
                    {
                        renderer.sharedMaterial = match.Material;
                        continue;
                    }

                    var changed = false;
                    for (var i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] && vacMaterialNames.Contains(CleanMaterialName(materials[i].name)))
                        {
                            materials[i] = match.Material;
                            changed = true;
                        }
                    }

                    if (changed)
                        renderer.sharedMaterials = materials;
                }
            }

            try
            {
                assignment.DetatchAttachmnet();
                if (match.Attachment)
                    assignment.AttachNewAttachment(match.Attachment);
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Vac palette attachment could not be changed: {ex.Message}");
            }
        }
    }

    private static Material? FindSlotMaterial(VacPalette palette, IntPtr slotPointer)
    {
        var slots = palette._materialSlots;
        if (slots == null)
            return null;

        foreach (var slot in slots)
        {
            if (slot != null && slot.Slot && slot.Slot.Pointer == slotPointer)
                return slot.Material;
        }

        return null;
    }

    [HideFromIl2Cpp]
    private static string CleanMaterialName(string name) => name.Replace(" (Instance)", string.Empty).Trim();

    // Neither path found the vac: the log shows what the avatar is made of, for the next build.
    private void LogAvatarMaterials()
    {
        var lines = new List<string>();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer)
                continue;

            var names = new List<string>();
            var materials = renderer.sharedMaterials;
            if (materials != null)
            {
                foreach (var material in materials)
                    names.Add(material ? material.name : "null");
            }

            lines.Add($"{renderer.gameObject.name}: {string.Join(", ", names)}");
            if (lines.Count >= 40)
                break;
        }

        SrLogger.LogMessage($"Avatar {ID} has no vac palette materials. Renderers: {string.Join(" | ", lines)}");
    }

    #endregion

    #region Hidden while in another scene

    private bool avatarHidden;
    private List<Renderer>? hiddenAvatarRenderers;

    // Only when both scene groups are known and differ, so a zone that cannot be mapped never
    // makes a player vanish. The marker hides itself.
    private void UpdateAvatarVisibility()
    {
        var hide = IsInOtherKnownSceneGroup();
        if (hide == avatarHidden)
            return;

        avatarHidden = hide;

        if (hide)
        {
            hiddenAvatarRenderers = new List<Renderer>();

            if (transform.childCount > 0)
            {
                foreach (var renderer in transform.GetChild(0).GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer && renderer.enabled)
                        hiddenAvatarRenderers.Add(renderer);
                }
            }

            var nameRenderer = UsernamePanel ? UsernamePanel.GetComponent<Renderer>() : null;
            if (nameRenderer && nameRenderer!.enabled)
                hiddenAvatarRenderers.Add(nameRenderer);

            // Only those that were on are turned back on later.
            foreach (var renderer in hiddenAvatarRenderers)
                renderer.enabled = false;

            return;
        }

        if (hiddenAvatarRenderers == null)
            return;

        foreach (var renderer in hiddenAvatarRenderers)
        {
            if (renderer)
                renderer.enabled = true;
        }

        hiddenAvatarRenderers = null;
    }

    #endregion

    #region Vac stream

    // How far in front of the hand the stream starts, to reach the end of the vac. A guess.
    private const float VacFXForwardOffset = 0.3f;
    private const float VacFXRetrySeconds = 2f;

    // How long the stream keeps showing after the vac stops, so its animation can wind down.
    private const float VacFXWindDownSeconds = 1f;

    private GameObject? vacFxAnchor;
    private GameObject? vacFxCopy;
    private GameObject? vacFxSource;
    private bool vacFxAligned;
    private ParticleSystem[]? vacFxParticles;
    private Animator[]? vacFxAnimators;
    private int vacFxActiveParam;
    private int vacFxVacModeParam;
    private bool vacFxActive;
    private bool vacFxOn;
    private float vacFxOffAt;
    private bool vacFxFailed;
    private float nextVacFxTry;

    private void UpdateVacFX()
    {
        if (IsLocal || vacFxFailed || model == null)
            return;

        var visible = !avatarHidden && IsLiveAvatar();
        var wanted = model.VacMode == (byte)VacuumItem.VacMode.VAC && visible;
        if (!wanted)
        {
            if (!visible || !vacFxActive)
            {
                SetVacFXActive(false);
                return;
            }

            // Let the stream's own animation wind down, then hide it.
            if (vacFxOn)
            {
                SetVacFXAnimating(false);
                vacFxOffAt = UnityEngine.Time.unscaledTime + VacFXWindDownSeconds;
            }
            else if (UnityEngine.Time.unscaledTime >= vacFxOffAt)
            {
                SetVacFXActive(false);
                return;
            }
        }

        try
        {
            if (!vacFxAnchor)
            {
                var now = UnityEngine.Time.unscaledTime;
                if (now < nextVacFxTry)
                    return;
                nextVacFxTry = now + VacFXRetrySeconds;

                if (!CreateVacFX())
                    return;
            }

            var rotation = transform.rotation * Quaternion.Euler(ReceivedLookY, 0, 0);
            var origin = rightHand
                ? rightHand.position
                : transform.position + (Vector3.up * (1.2f * transform.lossyScale.y));

            var aim = rotation * Vector3.forward;
            var anchor = vacFxAnchor!.transform;
            anchor.position = origin + (aim * NozzleDistance(origin, aim));
            anchor.rotation = rotation;

            if (wanted)
            {
                LogVacFXStateOnce();
                AlignVacConeOnce();

                SetVacFXActive(true);
                SetVacFXAnimating(true);
            }
        }
        catch (Exception ex)
        {
            vacFxFailed = true;
            SrLogger.LogWarning($"Vac stream on avatar {ID} turned off: {ex.Message}");
            DestroyVacFX();
        }
    }

    // A copy of the local player's own vac stream, with everything but the visuals taken off, so
    // it never pulls anything in on this machine.
    private bool CreateVacFX()
    {
        var vacuumItem = GetPlayerItemController()?._vacuumItem;
        if (!vacuumItem)
            return false;

        var source = vacuumItem!.VacFX;
        if (!source)
            return false;

        var vacOrigin = vacuumItem.VacOrigin ? vacuumItem.VacOrigin.transform : null;

        var anchorObject = new GameObject("SR2MP_RemoteVacFX");
        anchorObject.SetActive(false);
        anchorObject.transform.SetParent(transform, false);

        // Undo the avatar's scale, so the copy can take the source's world size as it is.
        var avatarScale = transform.lossyScale;
        anchorObject.transform.localScale = new Vector3(
            Mathf.Abs(avatarScale.x) > 1e-4f ? 1f / avatarScale.x : 1f,
            Mathf.Abs(avatarScale.y) > 1e-4f ? 1f / avatarScale.y : 1f,
            Mathf.Abs(avatarScale.z) > 1e-4f ? 1f / avatarScale.z : 1f);

        // Made under an inactive parent, so none of its scripts wake up before they are removed.
        var copy = Instantiate(source, anchorObject.transform);
        var componentNames = string.Join(", ", copy.GetComponentsInChildren<Component>(true)
            .Where(c => c)
            .Select(c => c.GetIl2CppType().Name)
            .Distinct());

        StripToFX(copy);
        EnableVacKeywordsOnCopy(copy, source);

        // The avatar's copy is often off to the side of this camera's view or starts folded up
        // small, and an Animator set to cull would then never run the swirl, so it never shows.
        foreach (var animator in copy.GetComponentsInChildren<Animator>(true))
        {
            if (animator)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        foreach (var skinned in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinned)
                skinned.updateWhenOffscreen = true;
        }

        LogVacFXRenderers(copy);
        copy.SetActive(true);

        // The anchor faces where the avatar looks, so the copy takes the stream's pose relative to
        // this player's own view (the stream points where the camera looks), not relative to the
        // vac's origin point, whose axes need not match the view: with those the cone stood upright.
        var copyTransform = copy.transform;
        var sourceTransform = source.transform;
        var view = source.GetComponentInParent<Camera>() is { } parentCamera && parentCamera
            ? parentCamera.transform
            : SceneContext.Instance && SceneContext.Instance.Camera ? SceneContext.Instance.Camera.transform : null;
        var viewRotation = view ? view!.rotation : vacOrigin ? vacOrigin!.rotation : sourceTransform.rotation;
        var inverse = Quaternion.Inverse(viewRotation);
        copyTransform.localPosition = vacOrigin ? inverse * (sourceTransform.position - vacOrigin!.position) : Vector3.zero;
        copyTransform.localRotation = inverse * sourceTransform.rotation;
        copyTransform.localScale = sourceTransform.lossyScale;
        vacFxCopy = copy;
        vacFxSource = source;
        vacFxAligned = false;

        // First-person objects can be on a layer only the player's own camera draws.
        if (transform.childCount > 0)
            SetLayerRecursively(copyTransform, transform.GetChild(0).gameObject.layer);

        vacFxParticles = copy.GetComponentsInChildren<ParticleSystem>(true);
        vacFxAnimators = copy.GetComponentsInChildren<Animator>(true);
        vacFxActiveParam = vacuumItem._animActiveId;
        vacFxVacModeParam = vacuumItem._animVacModeId;
        vacFxAnchor = anchorObject;
        vacFxActive = false;
        vacFxOn = false;

        SrLogger.LogMessage(
            $"Vac stream for avatar {ID}: {vacFxParticles.Length} particle systems, " +
            $"{copy.GetComponentsInChildren<Renderer>(true).Length} renderers, " +
            $"{vacFxAnimators.Length} animators, scale {sourceTransform.lossyScale}; " +
            $"source had: {componentNames}");
        return true;
    }

    // The cone's shader only draws while a global shader keyword is on, and the game turns that
    // keyword on only while this player's own vac runs (WorldParticleVacInteraction sets a scripted
    // bool, a ShaderGlobalKeywordController turns it into the keyword). So another player's cone
    // only showed while this player was vaccing too. The copy gets its own materials with the
    // keyword on: a keyword on the material counts even while the global one is off.
    private static List<string>? vacKeywords;

    private static List<string> VacKeywords(GameObject source)
    {
        if (vacKeywords is { Count: > 0 })
            return vacKeywords;

        var found = new List<string>();
        var log = new List<string>();
        try
        {
            var interaction = source.GetComponentInChildren<Il2CppMonomiPark.SlimeRancher.VFXGraphParticles.WorldParticleVacInteraction>(true);
            var vacBool = interaction ? interaction!.vacActiveScriptedBool : null;
            log.Add($"vac bool '{(vacBool ? vacBool!.name : "none")}'");

            foreach (var controller in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Rendering.ShaderGlobalKeywordController>())
            {
                if (!controller)
                    continue;

                var keyword = controller._keyword;
                var target = controller.targetValue;
                var drivenByVac = vacBool && target && target!.Pointer == vacBool!.Pointer;
                log.Add($"'{keyword}' from '{(target ? target!.name : "none")}'{(drivenByVac ? " (the vac's)" : "")} on={(!string.IsNullOrEmpty(keyword) && Shader.IsKeywordEnabled(keyword))}");

                if (drivenByVac && !string.IsNullOrEmpty(keyword) && !found.Contains(keyword))
                    found.Add(keyword);
            }

            // No controller tied to the vac's bool was found: take keywords that name the vac.
            if (found.Count == 0)
            {
                foreach (var controller in Resources.FindObjectsOfTypeAll<Il2CppMonomiPark.SlimeRancher.Rendering.ShaderGlobalKeywordController>())
                {
                    var keyword = controller ? controller._keyword : null;
                    if (!string.IsNullOrEmpty(keyword) && keyword!.Contains("VAC", StringComparison.OrdinalIgnoreCase) && !found.Contains(keyword))
                        found.Add(keyword);
                }
            }
        }
        catch (Exception ex)
        {
            log.Add($"failed: {ex.Message}");
        }

        SrLogger.LogMessage($"Vac stream keywords: using [{string.Join(", ", found)}]; seen: {string.Join(" | ", log)}");
        vacKeywords = found;
        return found;
    }

    private static bool ShaderHasKeyword(Shader shader, string keyword)
    {
        try
        {
            var found = shader.keywordSpace.FindKeyword(keyword);
            return found != null && found.isValid;
        }
        catch
        {
            return false;
        }
    }

    private void EnableVacKeywordsOnCopy(GameObject copy, GameObject source)
    {
        try
        {
            var keywords = VacKeywords(source);
            if (keywords.Count == 0)
                return;

            var done = new List<string>();
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer)
                    continue;

                var shared = renderer.sharedMaterials;
                if (shared == null || shared.Length == 0)
                    continue;

                var copies = new Material[shared.Length];
                var changed = false;
                for (var i = 0; i < shared.Length; i++)
                {
                    var material = shared[i];
                    copies[i] = material;
                    if (!material || !material.shader)
                        continue;

                    // keywordNames is not usable through the interop in this game; FindKeyword is.
                    var shader = material.shader;
                    var matching = keywords.Where(k => ShaderHasKeyword(shader, k)).ToList();
                    if (matching.Count == 0)
                        continue;

                    // A material of our own, so the player's own vac keeps the game's.
                    var own = new Material(material) { name = material.name + " (SR2MP remote)" };
                    foreach (var keyword in matching)
                        own.EnableKeyword(keyword);

                    copies[i] = own;
                    changed = true;
                    done.Add($"'{renderer.name}': {string.Join(", ", matching)}");
                }

                if (changed)
                    renderer.sharedMaterials = copies;
            }

            SrLogger.LogMessage($"Vac stream for avatar {ID}: keywords turned on for {(done.Count == 0 ? "no renderer (none of their shaders use them)" : string.Join(" | ", done))}");
        }
        catch (Exception ex)
        {
            SrLogger.LogWarning($"Vac stream for avatar {ID}: could not turn on the vac keywords: {ex.Message}");
        }
    }

    // The avatar's vac model: the renderers its vac palette colours. The stream starts at the
    // front end of these, along the aim, instead of a guessed distance from the hand.
    private List<Renderer>? vacGunRenderers;

    private float NozzleDistance(Vector3 origin, Vector3 aim)
    {
        if (vacGunRenderers == null)
        {
            vacGunRenderers = new List<Renderer>();
            try
            {
                if (!vacPaletteSetupDone)
                    SetupVacPaletteTargets();

                if (vacApplyPalettes != null)
                {
                    foreach (var applyPalette in vacApplyPalettes)
                    {
                        var assignments = applyPalette ? applyPalette.PaletteSlotAssignments : null;
                        if (assignments == null)
                            continue;

                        foreach (var assignment in assignments)
                        {
                            var renderers = assignment?.renderers;
                            if (renderers == null)
                                continue;

                            foreach (var renderer in renderers)
                            {
                                if (renderer && !vacGunRenderers.Contains(renderer))
                                    vacGunRenderers.Add(renderer);
                            }
                        }
                    }
                }

                if (vacMaterialUses != null)
                {
                    foreach (var use in vacMaterialUses)
                    {
                        if (use.Renderer && !vacGunRenderers.Contains(use.Renderer))
                            vacGunRenderers.Add(use.Renderer);
                    }
                }
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Vac model on avatar {ID}: {ex.Message}");
            }

            SrLogger.LogMessage($"Vac stream on avatar {ID} starts at the front of {vacGunRenderers.Count} vac model renderers");
        }

        var scale = Mathf.Abs(transform.lossyScale.z);
        var fallback = VacFXForwardOffset * scale;
        var furthest = 0f;
        foreach (var renderer in vacGunRenderers)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            // The furthest corner of the renderer's box along the aim.
            var bounds = renderer.bounds;
            var center = Vector3.Dot(bounds.center - origin, aim);
            var reach = Mathf.Abs(aim.x) * bounds.extents.x + Mathf.Abs(aim.y) * bounds.extents.y + Mathf.Abs(aim.z) * bounds.extents.z;
            furthest = Mathf.Max(furthest, center + reach);
        }

        // A box of a model held across the body can reach further than the barrel; keep it sane.
        return furthest > 0.05f * scale ? Mathf.Clamp(furthest, 0.1f * scale, 1.5f * scale) : fallback;
    }

    // Once per avatar: what the copy's renderers are, so a test log shows why a part may not show.
    private void LogVacFXRenderers(GameObject copy)
    {
        try
        {
            var root = copy.transform;
            var lines = new List<string>();
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer)
                    continue;

                var materials = renderer.sharedMaterials;
                var shaders = materials == null
                    ? "none"
                    : string.Join("/", materials.Where(m => m).Select(m => $"{m.name}[{(m.shader ? m.shader.name : "?")}]"));
                var line = $"{renderer.GetIl2CppType().Name} '{renderer.name}' enabled={renderer.enabled} layer={renderer.gameObject.layer} {shaders}";

                var skinned = renderer.TryCast<SkinnedMeshRenderer>();
                if (skinned)
                {
                    var bones = skinned!.bones;
                    var outside = bones == null ? 0 : bones.Count(b => b && !b.IsChildOf(root));
                    var rootBone = skinned.rootBone;
                    line += $" mesh={(skinned.sharedMesh ? skinned.sharedMesh.name : "none")} bones={bones?.Length ?? 0} outsideCopy={outside}" +
                            $" rootBone={(rootBone ? rootBone.name + (rootBone.IsChildOf(root) ? "" : " (outside the copy)") : "none")}";
                }

                lines.Add(line);
            }

            SrLogger.LogMessage($"Vac stream renderers for avatar {ID}: {string.Join(" | ", lines)}");
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not list vac stream renderers: {ex.Message}");
        }
    }

    // Once per avatar, a moment after the stream turns on: the cone's animation state and where
    // its renderers are, for the test log.
    private float vacFxStateLogAt = -1f;
    private bool vacFxStateLogged;

    private void LogVacFXStateOnce()
    {
        if (vacFxStateLogged || vacFxAnimators == null || !vacFxAnchor)
            return;

        var now = UnityEngine.Time.unscaledTime;
        if (vacFxStateLogAt < 0f)
        {
            vacFxStateLogAt = now + 1.5f;
            return;
        }

        if (now < vacFxStateLogAt)
            return;

        vacFxStateLogged = true;
        try
        {
            var anchorPosition = vacFxAnchor!.transform.position;
            var parts = new List<string>();
            foreach (var animator in vacFxAnimators)
            {
                if (!animator)
                    continue;

                var state = animator.GetCurrentAnimatorStateInfo(0);
                var values = animator.parameters.Select(p => p.type switch
                {
                    AnimatorControllerParameterType.Bool => $"{p.name}={animator.GetBool(p.nameHash)}",
                    AnimatorControllerParameterType.Float => $"{p.name}={animator.GetFloat(p.nameHash):0.##}",
                    _ => p.name,
                });
                parts.Add($"animator '{animator.name}' enabled={animator.isActiveAndEnabled} state={state.fullPathHash} t={state.normalizedTime:0.##} [{string.Join(", ", values)}]");
            }

            foreach (var renderer in vacFxAnchor.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer)
                    parts.Add($"'{renderer.name}' visible={renderer.isVisible} size={renderer.bounds.size} {Vector3.Distance(renderer.bounds.center, anchorPosition):0.##} m from the stream start");
            }

            SrLogger.LogMessage($"Vac stream state for avatar {ID}: {string.Join(" | ", parts)}");
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not log vac stream state: {ex.Message}");
        }
    }

    // Once the cone has grown out: if it does not point where the avatar aims, the copy is turned
    // so that it does. The log says which it was, so a test shows whether the pose was right.
    private float vacFxAlignAt = -1f;

    private void AlignVacConeOnce()
    {
        if (vacFxAligned || !vacFxAnchor || !vacFxCopy)
            return;

        var now = UnityEngine.Time.unscaledTime;
        if (vacFxAlignAt < 0f || now - vacFxAlignAt > 5f)
        {
            vacFxAlignAt = now + 1f;
            return;
        }

        if (now < vacFxAlignAt)
            return;

        try
        {
            // The cone is the stream's one big mesh; the particle systems are left out.
            Renderer? cone = null;
            var biggest = 0f;
            foreach (var renderer in vacFxCopy!.GetComponentsInChildren<Renderer>())
            {
                if (!renderer || !renderer.enabled || renderer.TryCast<ParticleSystemRenderer>() != null)
                    continue;

                var size = renderer.bounds.size.sqrMagnitude;
                if (size > biggest)
                {
                    biggest = size;
                    cone = renderer;
                }
            }

            // Measured from the copy's own origin (where the stream starts) and turned about it, so
            // the base stays where it is and only the direction changes.
            var anchor = vacFxAnchor!.transform;
            var copyTransform = vacFxCopy.transform;
            var direction = cone ? Quaternion.Inverse(anchor.rotation) * (cone!.bounds.center - copyTransform.position) : Vector3.zero;

            // Not grown out yet (the vac only just started): look again shortly.
            if (direction.magnitude < 0.5f)
            {
                vacFxAlignAt = now + 0.5f;
                return;
            }

            vacFxAligned = true;
            var angle = Vector3.Angle(direction, Vector3.forward);
            var ownCone = OwnConeSize();
            if (angle < 20f)
            {
                SrLogger.LogMessage($"Vac stream on avatar {ID} points along the aim ({angle:0}° off, look pitch {ReceivedLookY:0}°, cone size {cone!.bounds.size}{ownCone})");
                return;
            }

            var turn = Quaternion.FromToRotation(direction.normalized, Vector3.forward);
            copyTransform.localRotation = turn * copyTransform.localRotation;
            SrLogger.LogMessage($"Vac stream on avatar {ID} pointed {angle:0}° away from the aim (direction {direction.normalized}, look pitch {ReceivedLookY:0}°), turned to point along it (cone size {cone!.bounds.size}{ownCone})");
        }
        catch (Exception ex)
        {
            vacFxAligned = true;
            SrLogger.LogDebug($"Vac stream on avatar {ID}: aligning the cone failed: {ex.Message}");
        }
    }

    // This player's own cone, for comparing sizes in the log, when it is showing.
    private string OwnConeSize()
    {
        if (!vacFxSource)
            return string.Empty;

        foreach (var renderer in vacFxSource!.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (renderer && renderer.enabled)
                return $", this player's own cone {renderer.bounds.size}";
        }

        return string.Empty;
    }

    // The game's vac drives its stream through an Animator: the cone mesh and the particles only
    // show while the vac's "active" parameter is on. The copy keeps that Animator, so set it here.
    private void SetVacFXAnimating(bool on)
    {
        if (vacFxAnimators == null || vacFxOn == on)
            return;

        vacFxOn = on;
        SetVacFXEmitting(on);

        // As the game's VacuumItem.UpdateVacAnimators does for the vac's animators: "active" is on
        // while the vac runs, and "vacMode" tells pulling in from shooting. With only "active" on,
        // the cone played its shooting animation (a flash at the nozzle) instead of the swirl.
        foreach (var animator in vacFxAnimators)
        {
            if (!animator || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
                continue;

            try
            {
                foreach (var parameter in animator.parameters)
                {
                    if (parameter.nameHash == vacFxActiveParam || parameter.nameHash == vacFxVacModeParam)
                        SetParameter(animator, parameter, on);
                }
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Vac stream animator on avatar {ID}: {ex.Message}");
            }
        }
    }

    private static void SetParameter(Animator animator, AnimatorControllerParameter parameter, bool on)
    {
        var id = parameter.nameHash;
        switch (parameter.type)
        {
            case AnimatorControllerParameterType.Bool:
                animator.SetBool(id, on);
                break;
            case AnimatorControllerParameterType.Float:
                animator.SetFloat(id, on ? 1f : 0f);
                break;
            case AnimatorControllerParameterType.Trigger when on:
                animator.SetTrigger(id);
                break;
            case AnimatorControllerParameterType.Trigger:
                animator.ResetTrigger(id);
                break;
        }
    }

    // Once per avatar: the stream's Animator parameters, so a test log shows what drives it.
    private bool vacFxParamsLogged;

    private void LogVacFXParameters()
    {
        if (vacFxParamsLogged || vacFxAnimators == null)
            return;

        vacFxParamsLogged = true;
        try
        {
            var lines = vacFxAnimators
                .Where(a => a && a.runtimeAnimatorController != null)
                .Select(a => $"{a.name}: " + string.Join(", ", a.parameters.Select(p =>
                    $"{p.name}({p.type}{(p.nameHash == vacFxActiveParam ? ", the vac's active id" : p.nameHash == vacFxVacModeParam ? ", the vac's mode id" : "")})")));
            SrLogger.LogMessage($"Vac stream animators for avatar {ID}: {string.Join(" | ", lines)}");
        }
        catch (Exception ex)
        {
            SrLogger.LogDebug($"Could not list vac stream animator parameters: {ex.Message}");
        }
    }

    private void SetVacFXActive(bool active)
    {
        if (!vacFxAnchor || vacFxActive == active)
            return;

        vacFxActive = active;
        vacFxAnchor!.SetActive(active);

        // An Animator starts over from its default state when it is enabled again.
        vacFxOn = false;

        if (!active)
            return;

        LogVacFXParameters();

        if (vacFxParticles == null)
            return;

        // The game may start these from its vac script rather than on enable; start the top ones.
        SetVacFXEmitting(true);
    }

    // Starts the top particle systems (with their children), or stops them emitting so the
    // particles already out fade as they would on the player's own vac.
    private void SetVacFXEmitting(bool emitting)
    {
        if (vacFxParticles == null)
            return;

        foreach (var particles in vacFxParticles)
        {
            if (!particles)
                continue;

            var parent = particles.transform.parent;
            if (parent && parent.GetComponentInParent<ParticleSystem>())
                continue;

            try
            {
                if (emitting)
                {
                    if (!particles.isPlaying)
                        particles.Play(true);
                }
                else
                {
                    // Also stops child systems that loop under a top system that already ended.
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            catch (Exception ex)
            {
                SrLogger.LogDebug($"Vac stream particles on avatar {ID}: {ex.Message}");
            }
        }
    }

    private void DestroyVacFX()
    {
        if (vacFxAnchor)
        {
            // The copy's own materials (made for the vac keywords) go with it.
            foreach (var renderer in vacFxAnchor!.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer)
                    continue;

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material && material.name.EndsWith(" (SR2MP remote)", StringComparison.Ordinal))
                        Destroy(material);
                }
            }

            Destroy(vacFxAnchor);
        }

        vacFxAnchor = null;
        vacFxCopy = null;
        vacFxSource = null;
        vacFxParticles = null;
        vacFxAnimators = null;
        vacFxActive = false;
        vacFxOn = false;
    }

    private static void StripToFX(GameObject root)
    {
        // Scripts first: some of the other components may be required by one of them.
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour)
                DestroyImmediate(behaviour);
        }

        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null ||
                component.TryCast<Transform>() != null ||
                component.TryCast<Renderer>() != null ||
                component.TryCast<MeshFilter>() != null ||
                component.TryCast<ParticleSystem>() != null ||
                // The stream's Animator shows the cone and particles; the force field only moves
                // particles. Neither touches anything in the world.
                component.TryCast<Animator>() != null ||
                component.TryCast<ParticleSystemForceField>() != null ||
                component.TryCast<UnityEngine.VFX.VisualEffect>() != null)
                continue;

            DestroyImmediate(component);
        }
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (var i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }

    #endregion
}
