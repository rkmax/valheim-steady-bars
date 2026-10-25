using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SteadyBars;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "rkmax.steadybars";
    public const string Name = "SteadyBars";
    public const string Version = "0.1.0";

    internal static ConfigEntry<bool> ShowStamina;
    internal static ConfigEntry<bool> ShowEitr;

    private void Awake()
    {
        ShowStamina = Config.Bind("Bars", "ShowStamina", true,
            "Shows a stamina bar next to the health bar, always on screen.");
        ShowEitr = Config.Bind("Bars", "ShowEitr", true,
            "Shows an eitr bar next to the stamina bar, always on screen while your maximum eitr is above 0.");
        new Harmony(Guid).PatchAll();
    }
}

/// <summary>
/// Stamina and eitr bars built as copies of the health bar and placed to its right, sized and filled the
/// way Hud.UpdateHealth does it, each with a skill icon (Run, Elemental Magic) where the health bar has its
/// own icon. The game's own stamina and eitr bars are left as they are. The guardian
/// power icon, which sits right of the health bar, moves right by the room the copies take.
/// </summary>
[HarmonyPatch(typeof(Hud), "Update")]
internal static class SideBars
{
    // Between bars, in the health bar's units (it is 32 thick). Each frame reaches 4 past its bar, so 12
    // leaves 4 between frames.
    private const float Gap = 12f;

    private static Hud _hud;
    private static Bar _stamina, _eitr;

    private static void Postfix(Hud __instance)
    {
        if (_hud != __instance)
            Build(__instance);
        Player p = Player.m_localPlayer;
        if (p == null)
            return;
        _stamina?.Set(p, p.GetStamina(), p.GetMaxStamina(), true);
        _eitr?.Set(p, p.GetEitr(), p.GetMaxEitr(), p.GetMaxEitr() > 0f);
    }

    private static void Build(Hud hud)
    {
        _hud = hud;
        _stamina = _eitr = null;
        int slot = 0;
        if (Plugin.ShowStamina.Value)
            _stamina = new Bar(hud, ++slot, "SteadyBars_Stamina", hud.m_staminaBar2Fast, hud.m_staminaBar2Slow,
                Skills.SkillType.Run);
        if (Plugin.ShowEitr.Value)
            _eitr = new Bar(hud, ++slot, "SteadyBars_Eitr", hud.m_eitrBarFast, hud.m_eitrBarSlow,
                Skills.SkillType.ElementalMagic);
        if (slot == 0)
            return;

        // The power icon is a sibling of the health panel; move it right by the copies' thickness.
        RectTransform health = hud.m_healthBarRoot;
        float step = (health.rect.height + Gap) * hud.m_healthPanel.localScale.x;
        RectTransform gp = hud.m_gpRoot;
        gp.anchoredPosition += (Vector2)(gp.parent.InverseTransformDirection(Vector3.right) * step * slot);
    }

    private sealed class Bar
    {
        private readonly RectTransform _root;
        private readonly GuiBar _fast, _slow;
        private readonly TMP_Text _text;
        private readonly Image _icon;
        private readonly Skills.SkillType _skill;

        /// <summary>
        /// A copy of the health bar <paramref name="slot"/> bar-widths to its right, with the colours and fill
        /// animation of the game's own bar, and a copy of the health icon under it showing the icon of <paramref name="skill"/>.
        /// </summary>
        public Bar(Hud hud, int slot, string name, GuiBar matchFast, GuiBar matchSlow, Skills.SkillType skill)
        {
            RectTransform health = hud.m_healthBarRoot;
            GameObject copy = Object.Instantiate(health.gameObject, health.parent, false);
            copy.name = name;
            _root = (RectTransform)copy.transform;
            Vector2 offset = health.parent.InverseTransformDirection(Vector3.right) * (health.rect.height + Gap) * slot;
            _root.anchoredPosition = health.anchoredPosition + offset;

            _skill = skill;
            Transform healthIcon = hud.m_healthPanel.Find("healthicon");
            if (healthIcon != null)
            {
                var icon = (RectTransform)Object.Instantiate(healthIcon.gameObject, healthIcon.parent, false).transform;
                icon.name = name + "Icon";
                icon.anchoredPosition = ((RectTransform)healthIcon).anchoredPosition + offset;
                _icon = icon.GetComponent<Image>();
                _icon.color = Color.white; // The health icon is dimmed; skill icons keep their own colours.
                _icon.sprite = null;
            }

            // The copy keeps the health bar's layout: its fast and slow GuiBars sit in the same places.
            _fast = Twin(hud.m_healthBarFast, health, _root);
            _slow = Twin(hud.m_healthBarSlow, health, _root);
            Match(_fast, matchFast);
            Match(_slow, matchSlow);
            _text = Twin(hud.m_healthText, health, _root);
        }

        public void Set(Player player, float value, float max, bool show)
        {
            if (_root.gameObject.activeSelf != show)
                _root.gameObject.SetActive(show);
            if (_icon != null)
            {
                if (_icon.sprite == null)
                    _icon.sprite = SkillIcon(player, _skill);
                bool showIcon = show && _icon.sprite != null;
                if (_icon.gameObject.activeSelf != showIcon)
                    _icon.gameObject.SetActive(showIcon);
            }
            if (!show || max <= 0f)
                return;
            float size = Mathf.Ceil(max / 25f * 32f);
            _root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size);
            _fast.SetWidth(size);
            _slow.SetWidth(size);
            _fast.SetMaxValue(max);
            _fast.SetValue(value);
            _slow.SetMaxValue(max);
            _slow.SetValue(value);
            _text.text = Mathf.CeilToInt(value).ToString();
        }

        private static Sprite SkillIcon(Player player, Skills.SkillType skill)
        {
            foreach (Skills.SkillDef def in player.GetSkills().m_skills)
                if (def.m_skill == skill)
                    return def.m_icon;
            return null;
        }

        /// <summary>
        /// Takes the colour and the fill animation of the game's own bar. The health bar's animation waits
        /// 0.5 s after each rise, which a bar that regenerates every frame never gets past.
        /// </summary>
        private static void Match(GuiBar bar, GuiBar from)
        {
            bar.m_smoothDrain = from.m_smoothDrain;
            bar.m_smoothFill = from.m_smoothFill;
            bar.m_smoothSpeed = from.m_smoothSpeed;
            bar.m_changeDelay = from.m_changeDelay;
            Image to = bar.m_bar.GetComponent<Image>(), src = from.m_bar.GetComponent<Image>();
            if (to != null && src != null)
                to.color = src.color;
        }

        /// <summary>The component in the copy at the same path as <paramref name="original"/> under the health bar.</summary>
        private static T Twin<T>(T original, Transform health, Transform copy) where T : Component
        {
            string path = Path(original.transform, health);
            Transform t = path.Length == 0 ? copy : copy.Find(path);
            return t.GetComponent<T>();
        }

        private static string Path(Transform t, Transform root)
        {
            string path = "";
            for (; t != root; t = t.parent)
                path = path.Length == 0 ? t.name : t.name + "/" + path;
            return path;
        }
    }
}
