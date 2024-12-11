﻿using System.Drawing;
using Android.Runtime;
using Firebase.Analytics;
using Firebase.Crashlytics;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Droid.Ads.VhAdMob;
using VpnHood.Client.App.Droid.Ads.VhChartboost;
using VpnHood.Client.App.Droid.Ads.VhInMobi;
using VpnHood.Client.App.Droid.Common;
using VpnHood.Client.App.Droid.Common.Constants;
using VpnHood.Client.App.Droid.GooglePlay;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.Store;
using VpnHood.Common.Logging;

namespace VpnHood.Client.App.Android.Client.Google;

[Application(
    Label = AndroidAppConstants.Label,
    Icon = AndroidAppConstants.Icon,
    Banner = AndroidAppConstants.Banner,
    NetworkSecurityConfig = AndroidAppConstants.NetworkSecurityConfig,
    SupportsRtl = AndroidAppConstants.SupportsRtl,
    AllowBackup = AndroidAppConstants.AllowBackup)]
[MetaData("com.google.android.gms.ads.APPLICATION_ID", Value = AppConfigs.AdMobApplicationId)]
[MetaData("com.google.android.gms.ads.flag.OPTIMIZE_INITIALIZATION", Value = "true")]
[MetaData("com.google.android.gms.ads.flag.OPTIMIZE_AD_LOADING", Value = "true")]
public class App(IntPtr javaReference, JniHandleOwnership transfer)
    : VpnHoodAndroidApp(javaReference, transfer)
{
    private FirebaseAnalytics? _analytics;
    public static int? SpaDefaultPort => IsDebugMode ? 9581 : 9580;
    public static bool SpaListenToAllIps => IsDebugMode;

    protected override AppOptions CreateAppOptions()
    {
        // initialize Firebase services
        try { _analytics = FirebaseAnalytics.GetInstance(this); } catch { /* ignored*/ }
        try { FirebaseCrashlytics.Instance.SetCrashlyticsCollectionEnabled(Java.Lang.Boolean.True); } catch { /* ignored */ }

        // load app settings and resources
        var storageFolderPath = AppOptions.BuildStorageFolderPath(PackageName!);
        var resources = DefaultAppResource.Resources;
        resources.Strings.AppName = IsDebugMode ? "VpnHOOD! CONNECT (DEBUG)" : "VpnHood! CONNECT";
        resources.Colors.NavigationBarColor = Color.FromArgb(21, 14, 61);
        resources.Colors.WindowBackgroundColor = Color.FromArgb(21, 14, 61);
        resources.Colors.ProgressBarColor = Color.FromArgb(231, 180, 129);

        var appConfigs = AppConfigs.Load();
        return new AppOptions(appId: PackageName!, IsDebugMode) {
            StorageFolderPath = storageFolderPath,
            AccessKeys = [appConfigs.DefaultAccessKey],
            Resource = resources,
            UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodConnect-android.json"),
            UiName = "VpnHoodConnect",
            IsAddAccessKeySupported = false,
            UpdaterProvider = new GooglePlayAppUpdaterProvider(),
            AccountProvider = CreateAppAccountProvider(appConfigs, storageFolderPath),
            AdProviderItems = CreateAppAdProviderItems(appConfigs),
            AllowEndPointTracker = _analytics != null,
            Tracker = _analytics != null ? new AnalyticsTracker(_analytics) : null,
            AdOptions = new AppAdOptions {
                PreloadAd = true
            }
        };
    }

    // Set the clientId as userId to the analytics
    public override void OnCreate()
    {
        base.OnCreate();
        _analytics?.SetUserId(VpnHoodApp.Instance.Features.ClientId);
    }

    private static AppAdProviderItem[] CreateAppAdProviderItems(AppConfigs appConfigs)
    {
        return [
            new AppAdProviderItem {
                AdProvider = AdMobInterstitialAdProvider.Create(appConfigs.AdMobInterstitialAdUnitId),
                ExcludeCountryCodes = ["IR", "CN"],
                ProviderName = "AdMob",
            },

            new AppAdProviderItem {
                AdProvider = InMobiAdProvider.Create(appConfigs.InmobiAccountId, appConfigs.InmobiPlacementId, appConfigs.InmobiIsDebugMode),
                ProviderName = "InMobi",
            },

            new AppAdProviderItem {
                AdProvider = ChartboostAdProvider.Create(appConfigs.ChartboostAppId, appConfigs.ChartboostAppSignature, appConfigs.ChartboostAdLocation),
                ExcludeCountryCodes = ["IR", "CN"],
                ProviderName = "Chartboost",
            },

            new AppAdProviderItem {
                AdProvider = AdMobInterstitialAdProvider.Create(appConfigs.AdMobInterstitialNoVideoAdUnitId),
                ExcludeCountryCodes = ["CN"],
                ProviderName = "AdMob-NoVideo",
            },

            new AppAdProviderItem {
                AdProvider = AdMobRewardedAdProvider.Create(appConfigs.AdMobRewardedAdUnitId),
                ExcludeCountryCodes = ["IR", "CN"],
                ProviderName = "AdMob-Rewarded",
            },

        ];
    }

    private static StoreAccountProvider? CreateAppAccountProvider(AppConfigs appConfigs, string storageFolderPath)
    {
        try {
            var authenticationExternalProvider = new GooglePlayAuthenticationProvider(appConfigs.GoogleSignInClientId);
            var authenticationProvider = new StoreAuthenticationProvider(storageFolderPath, new Uri(appConfigs.StoreBaseUri),
                appConfigs.StoreAppId, authenticationExternalProvider, ignoreSslVerification: IsDebugMode);
            var googlePlayBillingProvider = new GooglePlayBillingProvider(authenticationProvider);
            var accountProvider = new StoreAccountProvider(authenticationProvider, googlePlayBillingProvider, appConfigs.StoreAppId);
            return accountProvider;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create AppAccountService.");
            return null;
        }
    }

#if DEBUG
    public static bool IsDebugMode => true;
#else
    public static bool IsDebugMode => false;
#endif
}
