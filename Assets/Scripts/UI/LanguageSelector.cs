using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;

[RequireComponent(typeof(PanelRenderer))]
public class LanguageSelector : MonoBehaviour
{
    private PanelRenderer panel;
    private DropdownField languageDropdown;
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

        ConfigureDropdown();
    }

    private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
    {
        if (languageDropdown != null)
        {
            languageDropdown.UnregisterValueChangedCallback(OnLanguageChanged);
        }

        languageDropdown = root.Q<DropdownField>("language-dropdown");

        if (languageDropdown == null)
        {
            Debug.LogError("Language dropdown was not found.");
            return;
        }

        languageDropdown.SetEnabled(false);

        if (isInitialized)
        {
            ConfigureDropdown();
        }
    }

    private void ConfigureDropdown()
    {
        if (languageDropdown == null)
        {
            return;
        }

        var choices = new List<string>();

        foreach (var locale in locales)
        {
            choices.Add(locale.Identifier.CultureInfo?.NativeName ?? locale.LocaleName);
        }

        languageDropdown.choices = choices;
        OnLocaleChanged(LocalizationSettings.SelectedLocale);

        languageDropdown.UnregisterValueChangedCallback(OnLanguageChanged);
        languageDropdown.RegisterValueChangedCallback(OnLanguageChanged);

        languageDropdown.SetEnabled(locales.Count > 0);
    }

    private void OnLanguageChanged(ChangeEvent<string> evt)
    {
        if (evt.target != languageDropdown)
        {
            return;
        }

        int index = languageDropdown.index;

        if (index < 0 || index >= locales.Count)
        {
            return;
        }

        var selectedLocale = locales[index];
        LocalizationSettings.SelectedLocale = selectedLocale;
    }

    private void OnLocaleChanged(Locale locale)
    {
        // The startup selector records the locale before this event is raised.
        PlayerPrefs.Save();

        if (languageDropdown == null)
        {
            return;
        }

        int index = locales.IndexOf(locale);

        if (index >= 0)
        {
            languageDropdown.SetValueWithoutNotify(languageDropdown.choices[index]);
        }
    }

    private void OnDestroy()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;

        if (languageDropdown != null)
        {
            languageDropdown.UnregisterValueChangedCallback(OnLanguageChanged);
        }

        if (panel != null)
        {
            panel.UnregisterUIReloadCallback(OnUIReload);
        }
    }
}
