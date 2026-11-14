using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;

// One themed key per available locale (no native dropdown popup); the selected locale's key is highlighted.
[RequireComponent(typeof(PanelRenderer))]
public class LanguageSelector : MonoBehaviour
{
    private const string SelectedClass = "pg-key-primary";
    private PanelRenderer panel;
    private VisualElement languageOptions;
    private readonly List<Button> buttons = new List<Button>();
    private List<Locale> locales;
    private bool isInitialized;

    private void Awake()
    {
        panel = GetComponent<PanelRenderer>();
        panel.RegisterUIReloadCallback(OnUIReload);
    }

    private IEnumerator Start()
    {
        var initialization = LocalizationSettings.InitializationOperation;
        yield return initialization;

        if (initialization.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError("Localization initialization failed.");
            yield break;
        }

        locales = new List<Locale>(LocalizationSettings.AvailableLocales.Locales);

        isInitialized = true;
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;

        BuildButtons();
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
    {
        languageOptions = root.Q<VisualElement>("language-options");

        if (languageOptions == null)
        {
            Debug.LogError("Language options were not found.");
            return;
        }

        buttons.Clear();
        languageOptions.Clear();

        if (isInitialized)
        {
            BuildButtons();
        }
    }

    private void BuildButtons()
    {
        if (languageOptions == null)
        {
            return;
        }

        buttons.Clear();
        languageOptions.Clear();

        foreach (var locale in locales)
        {
            var selected = locale;
            var button = new Button(() => LocalizationSettings.SelectedLocale = selected)
            {
                text = DisplayName(locale)
            };
            button.AddToClassList("pg-key");
            button.AddToClassList("language-button");
            languageOptions.Add(button);
            buttons.Add(button);
        }

        OnLocaleChanged(LocalizationSettings.SelectedLocale);
    }

    // Language name in its own language without the region: "Türkçe", "English".
    private static string DisplayName(Locale locale)
    {
        var culture = locale.Identifier.CultureInfo;

        if (culture == null)
        {
            return locale.LocaleName;
        }

        if (!culture.IsNeutralCulture && culture.Parent != null && !string.IsNullOrEmpty(culture.Parent.Name))
        {
            culture = culture.Parent;
        }

        string name = culture.NativeName;
        return name.Length > 0 ? char.ToUpper(name[0], culture) + name.Substring(1) : locale.LocaleName;
    }

    private void OnLocaleChanged(Locale locale)
    {
        // The startup selector records the locale before this event is raised.
        PlayerPrefs.Save();

        for (int i = 0; i < buttons.Count; i++)
        {
            buttons[i].EnableInClassList(SelectedClass, locales[i] == locale);
        }
    }

    private void OnDestroy()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;

        if (panel != null)
        {
            panel.UnregisterUIReloadCallback(OnUIReload);
        }
    }
}
