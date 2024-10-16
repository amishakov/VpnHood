﻿using VpnHood.Client.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Common.IpLocations;

namespace VpnHood.Client.App.Services;

internal class AppAdService(IRegionProvider regionProvider, App.AppAdService[] adServices, AppAdOptions adOptions) : 
    AppMultiAdService(adServices, adOptions), 
    IAdService
{
    public async Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var countryCode = await regionProvider.GetCurrentCountryCodeAsync(cancellationToken);
        await LoadAd(uiContext, countryCode: countryCode, forceReload: false, cancellationToken);
    }

    public async Task<ShowedAdResult> ShowAd(IUiContext uiContext, string sessionId, CancellationToken cancellationToken)
    {
        var adData = $"sid:{sessionId};ad:{Guid.NewGuid()}";
        var countryCode = await regionProvider.GetCurrentCountryCodeAsync(cancellationToken);
        await LoadAd(uiContext, countryCode: countryCode, forceReload: false, cancellationToken);
        var networkName = await ShowLoadedAd(uiContext, adData, cancellationToken);
        var showAdResult = new ShowedAdResult {
            AdData = adData,
            NetworkName = networkName
        };
        return showAdResult;
    }
}