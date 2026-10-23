using System;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Fragment.App;
using Google.Android.Material.Button;
using Google.Android.Material.MaterialSwitch;
using Google.Android.Material.TextField;
using SyncDroidAndroid.Services;

namespace SyncDroidAndroid.Fragments;

public class SettingsFragment : AndroidX.Fragment.App.Fragment
{
    private bool _suppressEvents;

    private LinearLayout? _layoutSettingsRoot;
    private LinearLayout? _layoutSettingsColumns;
    private LinearLayout? _paneSettingsLeft;
    private LinearLayout? _paneSettingsRight;

    private TextView? _txtLangTitle;
    private TextView? _txtLangDesc;
    private MaterialButtonToggleGroup? _toggleLanguage;

    private TextView? _txtThemeTitle;
    private TextView? _txtThemeDesc;
    private MaterialButtonToggleGroup? _toggleTheme;
    private MaterialButton? _btnThemeSystem;
    private MaterialButton? _btnThemeLight;
    private MaterialButton? _btnThemeDark;
    private MaterialSwitch? _switchDynamicColor;

    private TextView? _txtPermSectionTitle;
    private TextView? _txtStoragePermStatus;
    private MaterialButton? _btnGrantStoragePerm;
    private TextView? _txtPortLabel;
    private TextInputEditText? _inputCompanionPort;
    private MaterialButton? _btnSavePort;
    private MaterialSwitch? _switchLocalSendEnabled;
    private MaterialSwitch? _switchConfirmDelete;
    private MaterialSwitch? _switchCheckLongNames;

    private TextView? _txtAboutTitle;
    private TextView? _txtAboutDesc;
    private TextView? _txtAboutDeveloper;
    private MaterialButton? _btnGithub;
    private MaterialButton? _btnPrivacy;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_settings, container, false)!;

        _layoutSettingsRoot = view.FindViewById<LinearLayout>(Resource.Id.layout_settings_root);
        _layoutSettingsColumns = view.FindViewById<LinearLayout>(Resource.Id.layout_settings_columns);
        _paneSettingsLeft = view.FindViewById<LinearLayout>(Resource.Id.pane_settings_left);
        _paneSettingsRight = view.FindViewById<LinearLayout>(Resource.Id.pane_settings_right);

        _txtLangTitle = view.FindViewById<TextView>(Resource.Id.txt_lang_title);
        _txtLangDesc = view.FindViewById<TextView>(Resource.Id.txt_lang_desc);
        _toggleLanguage = view.FindViewById<MaterialButtonToggleGroup>(Resource.Id.toggle_language);

        _txtThemeTitle = view.FindViewById<TextView>(Resource.Id.txt_theme_title);
        _txtThemeDesc = view.FindViewById<TextView>(Resource.Id.txt_theme_desc);
        _toggleTheme = view.FindViewById<MaterialButtonToggleGroup>(Resource.Id.toggle_theme);
        _btnThemeSystem = view.FindViewById<MaterialButton>(Resource.Id.btn_theme_system);
        _btnThemeLight = view.FindViewById<MaterialButton>(Resource.Id.btn_theme_light);
        _btnThemeDark = view.FindViewById<MaterialButton>(Resource.Id.btn_theme_dark);
        _switchDynamicColor = view.FindViewById<MaterialSwitch>(Resource.Id.switch_dynamic_color);

        _txtPermSectionTitle = view.FindViewById<TextView>(Resource.Id.txt_perm_section_title);
        _txtStoragePermStatus = view.FindViewById<TextView>(Resource.Id.txt_storage_perm_status);
        _btnGrantStoragePerm = view.FindViewById<MaterialButton>(Resource.Id.btn_grant_storage_perm);
        _txtPortLabel = view.FindViewById<TextView>(Resource.Id.txt_port_label);
        _inputCompanionPort = view.FindViewById<TextInputEditText>(Resource.Id.input_companion_port);
        _btnSavePort = view.FindViewById<MaterialButton>(Resource.Id.btn_save_port);
        _switchLocalSendEnabled = view.FindViewById<MaterialSwitch>(Resource.Id.switch_localsend_enabled);
        _switchConfirmDelete = view.FindViewById<MaterialSwitch>(Resource.Id.switch_confirm_delete);
        _switchCheckLongNames = view.FindViewById<MaterialSwitch>(Resource.Id.switch_check_long_names);

        _txtAboutTitle = view.FindViewById<TextView>(Resource.Id.txt_about_title);
        _txtAboutDesc = view.FindViewById<TextView>(Resource.Id.txt_about_desc);
        _txtAboutDeveloper = view.FindViewById<TextView>(Resource.Id.txt_about_developer);
        _btnGithub = view.FindViewById<MaterialButton>(Resource.Id.btn_github);
        _btnPrivacy = view.FindViewById<MaterialButton>(Resource.Id.btn_privacy);

        LoadSettingsToControls();
        WireEvents();
        ApplyLocalization();
        ApplyAdaptiveLayout(ResponsiveWindowService.Instance.CurrentState);

        return view;
    }

    public override void OnResume()
    {
        base.OnResume();
        ApplyAdaptiveLayout(ResponsiveWindowService.Instance.CurrentState);
        ResponsiveWindowService.Instance.WindowStateChanged += ApplyAdaptiveLayout;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdatePermissionStatusText();
    }

    public override void OnPause()
    {
        ResponsiveWindowService.Instance.WindowStateChanged -= ApplyAdaptiveLayout;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnPause();
    }

    private void ApplyAdaptiveLayout(AdaptiveWindowState state)
    {
        ResponsiveWindowService.ApplyTwoPaneLayout(
            Context,
            _layoutSettingsRoot,
            _layoutSettingsColumns,
            _paneSettingsLeft,
            _paneSettingsRight);
    }

    private void LoadSettingsToControls()
    {
        _suppressEvents = true;
        var settings = SettingsService.Load();

        int langBtnId = settings.Language switch
        {
            "zh-CN" => Resource.Id.btn_lang_zh_cn,
            "zh-TW" => Resource.Id.btn_lang_zh_tw,
            _ => Resource.Id.btn_lang_en
        };
        _toggleLanguage?.Check(langBtnId);

        int themeBtnId = settings.ThemeMode switch
        {
            "Light" => Resource.Id.btn_theme_light,
            "Dark" => Resource.Id.btn_theme_dark,
            _ => Resource.Id.btn_theme_system
        };
        _toggleTheme?.Check(themeBtnId);

        if (_switchDynamicColor != null) _switchDynamicColor.Checked = settings.UseDynamicColor;
        if (_inputCompanionPort != null) _inputCompanionPort.Text = settings.CompanionPort.ToString();
        if (_switchLocalSendEnabled != null) _switchLocalSendEnabled.Checked = settings.LocalSendEnabled;
        if (_switchConfirmDelete != null) _switchConfirmDelete.Checked = settings.ConfirmBeforeMirDelete;
        if (_switchCheckLongNames != null) _switchCheckLongNames.Checked = settings.CheckLongFileNames;

        _suppressEvents = false;
    }

    private void WireEvents()
    {
        _toggleLanguage!.ButtonChecked += (_, _) =>
        {
            if (_suppressEvents) return;
            int checkedId = _toggleLanguage.CheckedButtonId;
            string lang = checkedId switch
            {
                Resource.Id.btn_lang_zh_cn => "zh-CN",
                Resource.Id.btn_lang_zh_tw => "zh-TW",
                _ => "en-US"
            };
            LocalizationService.Instance.CurrentLanguage = lang;
        };

        _toggleTheme!.ButtonChecked += (_, _) =>
        {
            if (_suppressEvents) return;
            int checkedId = _toggleTheme.CheckedButtonId;
            string mode = checkedId switch
            {
                Resource.Id.btn_theme_light => "Light",
                Resource.Id.btn_theme_dark => "Dark",
                _ => "System"
            };

            var settings = SettingsService.Load();
            settings.ThemeMode = mode;
            SettingsService.Save(settings);

            MainActivity.ApplyThemeMode(mode);
        };

        _switchDynamicColor!.CheckedChange += (_, e) =>
        {
            if (_suppressEvents) return;
            var settings = SettingsService.Load();
            settings.UseDynamicColor = e.IsChecked;
            SettingsService.Save(settings);
            Activity?.Recreate();
        };

        _btnGrantStoragePerm!.Click += (_, _) =>
        {
            (Activity as MainActivity)?.RequestAllFilesAccessPermission();
        };

        _btnSavePort!.Click += (_, _) =>
        {
            if (int.TryParse(_inputCompanionPort?.Text?.Trim(), out int port) && port > 1024 && port <= 65535)
            {
                var settings = SettingsService.Load();
                settings.CompanionPort = port;
                SettingsService.Save(settings);

                if (CompanionNetworkService.Instance.IsServerRunning)
                {
                    CompanionNetworkService.Instance.StartServer(port);
                }

                if (Context != null)
                {
                    Toast.MakeText(Context, $"Companion TCP port updated to {port}", ToastLength.Short)?.Show();
                }
            }
        };

        _switchLocalSendEnabled!.CheckedChange += (_, e) =>
        {
            if (_suppressEvents) return;
            var settings = SettingsService.Load();
            settings.LocalSendEnabled = e.IsChecked;
            SettingsService.Save(settings);

            if (e.IsChecked && CompanionNetworkService.Instance.IsServerRunning)
            {
                LocalSendService.Instance.Start(settings.LocalSendPort);
            }
            else if (!e.IsChecked)
            {
                LocalSendService.Instance.Stop();
            }
        };

        _switchConfirmDelete!.CheckedChange += (_, e) =>
        {
            if (_suppressEvents) return;
            var settings = SettingsService.Load();
            settings.ConfirmBeforeMirDelete = e.IsChecked;
            SettingsService.Save(settings);
        };

        _switchCheckLongNames!.CheckedChange += (_, e) =>
        {
            if (_suppressEvents) return;
            var settings = SettingsService.Load();
            settings.CheckLongFileNames = e.IsChecked;
            SettingsService.Save(settings);
        };

        _btnGithub!.Click += (_, _) => OpenUrl("https://github.com/Hydro3ia/SyncDroid");
        _btnPrivacy!.Click += (_, _) => OpenUrl("https://github.com/Hydro3ia/SyncDroid/blob/main/PRIVACY.md");
    }

    private void OpenUrl(string url)
    {
        try
        {
            var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
            StartActivity(intent);
        }
        catch { }
    }

    private void UpdatePermissionStatusText()
    {
        if (_txtStoragePermStatus == null) return;
        var loc = LocalizationService.Instance;
        bool granted = MainActivity.HasAllFilesAccessPermission();
        _txtStoragePermStatus.Text = granted
            ? loc.T("All Files Access: Granted ✓", "所有文件访问权限：已授权 ✓", "所有檔案存取權限：已授權 ✓")
            : loc.T("All Files Access: Not Granted (Tap to grant)", "所有文件访问权限：未授权（点击右侧授权）", "所有檔案存取權限：未授權（點擊右側授權）");
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLocalization();

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        if (_txtLangTitle != null) _txtLangTitle.Text = loc.LanguageSectionTitle;
        if (_txtLangDesc != null) _txtLangDesc.Text = loc.LanguageSectionDesc;
        if (_txtThemeTitle != null) _txtThemeTitle.Text = loc.ThemeSectionTitle;
        if (_txtThemeDesc != null) _txtThemeDesc.Text = loc.ThemeSectionDesc;
        if (_btnThemeSystem != null) _btnThemeSystem.Text = loc.ThemeSystem;
        if (_btnThemeLight != null) _btnThemeLight.Text = loc.ThemeLight;
        if (_btnThemeDark != null) _btnThemeDark.Text = loc.ThemeDark;
        if (_switchDynamicColor != null) _switchDynamicColor.Text = loc.DynamicColorSwitch;

        if (_txtPermSectionTitle != null) _txtPermSectionTitle.Text = loc.PermSectionTitle;
        if (_btnGrantStoragePerm != null) _btnGrantStoragePerm.Text = loc.ManagePermButton;
        if (_txtPortLabel != null) _txtPortLabel.Text = loc.PortLabel;
        if (_btnSavePort != null) _btnSavePort.Text = loc.ApplyPortButton;
        if (_switchLocalSendEnabled != null) _switchLocalSendEnabled.Text = loc.LocalSendSwitchLabel;
        if (_switchConfirmDelete != null) _switchConfirmDelete.Text = loc.ConfirmDeleteSwitch;
        if (_switchCheckLongNames != null) _switchCheckLongNames.Text = loc.CheckLongNamesSwitch;

        if (_txtAboutTitle != null) _txtAboutTitle.Text = loc.AboutSectionTitle;
        if (_txtAboutDesc != null) _txtAboutDesc.Text = loc.AboutAppDescription;
        if (_txtAboutDeveloper != null) _txtAboutDeveloper.Text = loc.AboutDeveloperInfo;
        if (_btnGithub != null) _btnGithub.Text = loc.GithubButton;
        if (_btnPrivacy != null) _btnPrivacy.Text = loc.PrivacyButton;

        UpdatePermissionStatusText();
    }
}
