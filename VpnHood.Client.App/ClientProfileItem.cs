﻿using System;
using VpnHood.Common;

namespace VpnHood.Client.App;

public class ClientProfileItem
{
    public Guid ClientProfileId => ClientProfile.ClientProfileId;
    public required ClientProfile ClientProfile { get; init; }
    public required Token Token { get; init; }
    public string? Name => ClientProfile.Name ?? Token.Name;
}