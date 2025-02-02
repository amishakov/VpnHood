﻿using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using EmbedIO;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.IpLocations.Providers;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Utils;
using VpnHood.Test;
using VpnHood.Test.Device;
using VpnHood.Test.Tests;

// ReSharper disable DisposeOnUsingVariable

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class ClientAppTest : TestBase
{
    private static async Task UpdateIp2LocationFile()
    {
        // update current ipLocation in app project after a week
        var solutionFolder = TestHelper.GetParentDirectory(Directory.GetCurrentDirectory(), 5);
        var ipLocationFile = Path.Combine(solutionFolder, "Src/AppLib/VpnHood.AppLib.App/Resources", "IpLocations.zip");

        // find token
        var userSecretFile = Path.Combine(Path.GetDirectoryName(solutionFolder)!, ".user", "credentials.json");
        var document = JsonDocument.Parse(await File.ReadAllTextAsync(userSecretFile));
        var ip2LocationToken = document.RootElement.GetProperty("Ip2LocationToken").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(ip2LocationToken);

        await Ip2LocationDbParser.UpdateLocalDb(ipLocationFile, ip2LocationToken, forIpRange: true);
    }

    [TestMethod]
    public async Task IpLocations_must_be_loaded()
    {
        await UpdateIp2LocationFile();

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UseInternalLocationService = true;
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        var countryCodes = await app.IpRangeLocationProvider.GetCountryCodes();
        Assert.IsTrue(countryCodes.Any(x => x == "US"),
            "Countries has not been extracted.");

        // make sure GetIpRange works
        Assert.IsTrue((await app.IpRangeLocationProvider.GetIpRanges("US")).Any());
    }


    [TestMethod]
    public async Task State_Diagnose_info()
    {
        // create server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create app
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // ************
        // Test: With diagnose
        await app.Connect(clientProfile1.ClientProfileId, diagnose: true);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.Connected, 10000);
        await app.Disconnect(true);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.None);

        Assert.IsTrue(app.State.LogExists);
        Assert.IsTrue(app.State.HasDiagnoseRequested);
        Assert.IsTrue(app.State.HasDisconnectedByUser);
        Assert.IsTrue(app.State.HasProblemDetected);
        Assert.IsTrue(app.State.IsIdle);

        app.ClearLastError();
        Assert.IsNull(app.State.LastError);
        Assert.IsFalse(app.State.HasProblemDetected);

        // ************
        // Test: Without diagnose
        await app.Connect(clientProfile1.ClientProfileId);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.Connected);
        await app.Disconnect(true);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.None);

        Assert.IsTrue(app.State.IsIdle);
        Assert.IsFalse(app.State.HasDiagnoseRequested);
        Assert.IsTrue(app.State.HasDisconnectedByUser);
        Assert.IsTrue(app.State.LogExists);
    }

    [TestMethod]
    public async Task State_Error_InConnecting()
    {
        // create server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        token.ServerToken.HostEndPoints = [IPEndPoint.Parse("10.10.10.99:443")];

        // create app
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await Assert.ThrowsExceptionAsync<TimeoutException>(() => app.Connect(clientProfile.ClientProfileId));

        await TestAppHelper.WaitForAppState(app, AppConnectionState.None);
        Assert.IsTrue(app.State.LogExists);
        Assert.IsFalse(app.State.HasDiagnoseRequested);
        Assert.IsTrue(app.State.HasProblemDetected);
        Assert.IsNotNull(app.State.LastError);
    }


    [TestMethod]
    public async Task State_Waiting()
    {
        // create Access Manager and token
        using var accessManager = TestHelper.CreateAccessManager();
        var token = TestHelper.CreateAccessToken(accessManager);

        // create server
        await using var server1 = await TestHelper.CreateServer(accessManager);

        // create app & connect
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.SessionTimeout = TimeSpan.FromSeconds(20);
        appOptions.ReconnectTimeout = TimeSpan.FromSeconds(1);
        appOptions.AutoWaitTimeout = TimeSpan.FromSeconds(2);
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions, device: TestHelper.CreateDevice());
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.Connected);

        // dispose server and wait for waiting state
        await server1.DisposeAsync();
        await VhTestUtil.AssertEqualsWait(AppConnectionState.Waiting, async () => {
            await TestHelper.Test_Https(throwError: false, timeout: 100);
            return app.State.ConnectionState;
        });

        // start a new server & waiting for connected state
        await using var server2 = await TestHelper.CreateServer(accessManager);
        await VhTestUtil.AssertEqualsWait(AppConnectionState.Connected, async () => {
            await TestHelper.Test_Https(throwError: false, timeout: 100);
            return app.State.ConnectionState;
        });
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task IpFilters(bool isDnsServerSupported, bool usePassthru)
    {
        var testDns =
            !isDnsServerSupported; //dns will work as normal UDP when DnsServerSupported, otherwise it should be redirected

        // Create Server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create app
        var deviceOptions = new TestPacketCaptureOptions {
            CanSendPacketToOutbound = usePassthru,
            IsDnsServerSupported = isDnsServerSupported,
            CaptureDnsAddresses = TestHelper.TestIpAddresses.ToArray()
        };

        await using var app = TestAppHelper.CreateClientApp(device: TestHelper.CreateDevice(deviceOptions));
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        var customIps = (await Dns.GetHostAddressesAsync(TestConstants.HttpsUri1.Host))
            .Select(x => new IpRange(x))
            .Concat([
                new IpRange(TestConstants.PingV4Address1),
                new IpRange(TestConstants.NsEndPoint1.Address),
                new IpRange(TestConstants.UdpV4EndPoint1.Address),
                new IpRange(TestConstants.UdpV6EndPoint1.Address)
            ])
            .ToArray();

        // ************
        // *** TEST ***: Test Include ip filter
        app.SettingsService.IpFilterSettings.AppIpFilterIncludes = customIps.ToText();
        app.SettingsService.IpFilterSettings.AppIpFilterExcludes = "";
        await app.Connect(clientProfile.ClientProfileId);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.Connected);
        await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address1);

        await IpFilters_TestInclude(app, testPing: usePassthru, testUdp: true, testDns: testDns);
        await app.Disconnect();

        // ************
        // *** TEST ***: Test Exclude ip filters
        app.SettingsService.IpFilterSettings.AppIpFilterIncludes = "";
        app.SettingsService.IpFilterSettings.AppIpFilterExcludes = customIps.ToText();
        await app.Connect(clientProfile.ClientProfileId);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.Connected);

        await IpFilters_TestExclude(app, testPing: usePassthru, testUdp: true, testDns: testDns);
        await app.Disconnect();
    }

    public static async Task IpFilters_TestInclude(VpnHoodApp app, bool testUdp, bool testPing, bool testDns)
    {
        // TCP
        var oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri1);
        Assert.AreNotEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);

        // TCP
        oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri2);
        Assert.AreEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);

        if (testPing) {
            // ping
            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address1);
            Assert.AreNotEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);

            // ping
            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            try {
                await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address2, timeout: 1000);
                Assert.Fail("Exception expected as server should not exists.");
            }
            catch (Exception ex) {
                Assert.AreEqual(nameof(PingException), ex.GetType().Name);
            }

            Assert.AreEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);
        }

        if (testUdp) {
            // UDP
            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            await TestHelper.Test_Udp(TestConstants.UdpV4EndPoint1);
            Assert.AreNotEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);

            // UDP
            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            try {
                await TestHelper.Test_Udp(TestConstants.UdpV4EndPoint2, timeout: 1000);
                Assert.Fail("Exception expected as server should not exists.");
            }
            catch (Exception ex) {
                Assert.AreEqual(nameof(OperationCanceledException), ex.GetType().Name);
            }

            Assert.AreEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);
        }

        // DNS should always use tunnel regarding of any exclude or include option
        if (testDns) {
            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            await TestHelper.Test_Dns(nsEndPoint: TestConstants.NsEndPoint1);
            Assert.AreNotEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);

            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            await TestHelper.Test_Dns(nsEndPoint: TestConstants.NsEndPoint2);
            Assert.AreNotEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);
        }
    }

    public static async Task IpFilters_TestExclude(VpnHoodApp app, bool testUdp, bool testPing, bool testDns)
    {
        // TCP
        var oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri1);
        Assert.AreEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);

        // TCP
        oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri2);
        Assert.AreNotEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);

        if (testPing) {
            // ping
            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            try {
                await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address1, timeout: 1000);
                Assert.Fail("Exception expected as server should not exists.");
            }
            catch (Exception ex) {
                Assert.AreEqual(nameof(PingException), ex.GetType().Name);
            }

            Assert.AreEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);

            // ping
            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            await TestHelper.Test_Ping(ipAddress: TestConstants.PingV4Address2);
            Assert.AreNotEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);
        }

        if (testUdp) {
            // UDP
            VhLogger.Instance.LogTrace("Testing UDP include...");
            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            try {
                await TestHelper.Test_Udp(udpEndPoint: TestConstants.UdpV4EndPoint1, timeout: 1000);
                Assert.Fail("Exception expected as server should not exists.");
            }
            catch (Exception ex) {
                Assert.AreEqual(nameof(OperationCanceledException), ex.GetType().Name);
            }

            Assert.AreEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);

            // UDP
            VhLogger.Instance.LogTrace("Testing UDP exclude...");
            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            await TestHelper.Test_Udp(TestConstants.UdpV4EndPoint2);
            Assert.AreNotEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);
        }

        // DNS should always use tunnel regarding of any exclude or include option
        if (testDns) {
            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            await TestHelper.Test_Dns(nsEndPoint: TestConstants.NsEndPoint1);
            Assert.AreNotEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);

            oldReceivedByteCount = app.GetSessionStatus().SessionTraffic.Received;
            await TestHelper.Test_Dns(nsEndPoint: TestConstants.NsEndPoint2);
            Assert.AreNotEqual(oldReceivedByteCount, app.GetSessionStatus().SessionTraffic.Received);
        }
    }

    [TestMethod]
    public async Task State_Connected_Disconnected_successfully()
    {
        // create server
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // create app
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        await app.Connect(clientProfile.ClientProfileId);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.Connected);

        // get data through tunnel
        await TestHelper.Test_Https();

        Assert.IsTrue(app.State.LogExists);
        Assert.IsFalse(app.State.HasDiagnoseRequested);
        Assert.IsFalse(app.State.HasProblemDetected);
        Assert.IsNull(app.State.LastError);
        Assert.IsFalse(app.State.IsIdle);

        // test disconnect
        await app.Disconnect();
        await TestAppHelper.WaitForAppState(app, AppConnectionState.None);
    }

    [TestMethod]
    public async Task update_server_token_url_from_server()
    {
        // create Access Manager and token
        using var accessManager = TestHelper.CreateAccessManager();
        var token = TestHelper.CreateAccessToken(accessManager);

        // Update ServerTokenUrl after token creation
        const string newTokenUrl = "http://127.0.0.100:6000";
        accessManager.ServerConfig.ServerTokenUrls = [newTokenUrl];
        accessManager.ServerConfig.ServerSecret = VhUtil.GenerateKey(); // It can not be changed in new version
        accessManager.ClearCache();

        // create server and app
        await using var server = await TestHelper.CreateServer(accessManager);
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // wait for connect
        await app.Connect(clientProfile1.ClientProfileId);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.Connected);

        Assert.AreEqual(accessManager.ServerConfig.ServerTokenUrls.First(),
            app.ClientProfileService.GetToken(token.TokenId).ServerToken.Urls?.First());

        CollectionAssert.AreEqual(accessManager.ServerConfig.ServerSecret,
            app.ClientProfileService.GetToken(token.TokenId).ServerToken.Secret);
    }

    [TestMethod]
    public async Task update_token_from_server()
    {
        // create Access Manager and token
        using var accessManager = TestHelper.CreateAccessManager();
        var token = TestHelper.CreateAccessToken(accessManager, expirationTime: DateTime.UtcNow.AddDays(-1));
        var orgTokenName = token.Name;

        // Update ServerTokenUrl after token creation
        token.Name = Guid.NewGuid().ToString();

        // create server and app
        await using var server = await TestHelper.CreateServer(accessManager);
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());

        // wait for connect error
        var ex = await Assert.ThrowsExceptionAsync<SessionException>(()=>app.Connect(clientProfile1.ClientProfileId));
        Assert.AreEqual(SessionErrorCode.AccessExpired, ex.SessionResponse.ErrorCode);

        // token name must be updated
        var token2 = app.ClientProfileService.GetToken(token.TokenId);
        Assert.AreEqual(orgTokenName, token2.Name);

    }

    [TestMethod]
    public async Task update_server_token_from_server_token_url()
    {
        // create update webserver
        var endPoint1 = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback);
        var endPoint2 = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback);
        using var webServer1 = new WebServer(endPoint1.Port);
        using var webServer2 = new WebServer(endPoint2.Port);

        // create server1
        var tcpEndPoint = VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback);
        var fileAccessManagerOptions1 = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions1.TcpEndPoints = [tcpEndPoint];
        fileAccessManagerOptions1.ServerTokenUrls = [$"http://{endPoint1}/accesskey", $"http://{endPoint2}/accesskey"];
        using var accessManager1 = TestHelper.CreateAccessManager(fileAccessManagerOptions1);
        await using var server1 = await TestHelper.CreateServer(accessManager1);
        var token1 = TestHelper.CreateAccessToken(server1);
        await server1.DisposeAsync();

        // create server 2
        await Task.Delay(1100); // wait for new CreatedTime
        fileAccessManagerOptions1.TcpEndPoints = [VhUtil.GetFreeTcpEndPoint(IPAddress.Loopback, tcpEndPoint.Port + 1)];
        var accessManager2 = TestHelper.CreateAccessManager(storagePath: accessManager1.StoragePath,
            options: fileAccessManagerOptions1);
        await using var server2 = await TestHelper.CreateServer(accessManager2);
        var token2 = TestHelper.CreateAccessToken(server2);

        //update web server enc_server_token
        var isTokenRetrieved = false;
        webServer1.WithAction("/accesskey", HttpVerbs.Get, context => {
            isTokenRetrieved = true;
            return context.SendStringAsync("something_wrong", "text/plain", Encoding.UTF8);
        });

        webServer2.WithAction("/accesskey", HttpVerbs.Get, context => {
            isTokenRetrieved = true;
            return context.SendStringAsync(token2.ServerToken.Encrypt(), "text/plain", Encoding.UTF8);
        });

        webServer1.Start();
        webServer2.Start();


        // connect
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId);

        Assert.IsTrue(isTokenRetrieved);
        Assert.AreNotEqual(token1.ServerToken.CreatedTime, token2.ServerToken.CreatedTime);
        Assert.AreEqual(token2.ServerToken.CreatedTime,
            app.ClientProfileService.GetToken(token1.TokenId).ServerToken.CreatedTime);
        Assert.AreEqual(AppConnectionState.Connected, app.State.ConnectionState);
    }

    [TestMethod]
    public async Task Change_server_while_connected()
    {
        await using var server1 = await TestHelper.CreateServer();
        await using var server2 = await TestHelper.CreateServer();

        var token1 = TestHelper.CreateAccessToken(server1);
        var token2 = TestHelper.CreateAccessToken(server2);

        // connect
        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile1 = app.ClientProfileService.ImportAccessKey(token1.ToAccessKey());
        var clientProfile2 = app.ClientProfileService.ImportAccessKey(token2.ToAccessKey());

        await app.Connect(clientProfile1.ClientProfileId);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.Connected);

        await app.Connect(clientProfile2.ClientProfileId);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.Connected);

        Assert.AreEqual(AppConnectionState.Connected, app.State.ConnectionState,
            "Client connection has not been changed!");
    }

    [TestMethod]
    public async Task IncludeDomains()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();

        // create app
        var packetCaptureOptions = TestHelper.CreateTestPacketCaptureOptions();
        packetCaptureOptions.CanSendPacketToOutbound = false;
        await using var app = TestAppHelper.CreateClientApp(device: TestHelper.CreateDevice(packetCaptureOptions));
        app.UserSettings.DomainFilter.Excludes = [TestConstants.HttpsUri1.Host];

        // connect
        var token = TestHelper.CreateAccessToken(server);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId, diagnose: true);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.Connected);

        // text include
        var oldTcpTunnelledCount = app.GetSessionStatus().TcpTunnelledCount;
        var oldTcpPassthruCount = app.GetSessionStatus().TcpPassthruCount;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri1);
        Assert.AreEqual(oldTcpTunnelledCount, app.GetSessionStatus().TcpTunnelledCount);
        Assert.AreEqual(oldTcpPassthruCount + 1, app.GetSessionStatus().TcpPassthruCount);

        // text exclude
        oldTcpTunnelledCount = app.GetSessionStatus().TcpTunnelledCount;
        oldTcpPassthruCount = app.GetSessionStatus().TcpPassthruCount;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri2);
        Assert.AreEqual(oldTcpTunnelledCount + 1, app.GetSessionStatus().TcpTunnelledCount);
        Assert.AreEqual(oldTcpPassthruCount, app.GetSessionStatus().TcpPassthruCount);
    }

    [TestMethod]
    public async Task ExcludeDomains()
    {
        // Create Server
        await using var server = await TestHelper.CreateServer();

        // create app
        var packetCaptureOptions = TestHelper.CreateTestPacketCaptureOptions();
        packetCaptureOptions.CanSendPacketToOutbound = false;
        await using var app = TestAppHelper.CreateClientApp(device: TestHelper.CreateDevice(packetCaptureOptions));
        app.UserSettings.DomainFilter.Excludes = [TestConstants.HttpsUri1.Host];

        // connect
        var token = TestHelper.CreateAccessToken(server);
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId, diagnose: true);
        await TestAppHelper.WaitForAppState(app, AppConnectionState.Connected);

        // text include
        var oldTcpTunnelledCount = app.GetSessionStatus().TcpTunnelledCount;
        var oldTcpPassthruCount = app.GetSessionStatus().TcpPassthruCount;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri2);
        Assert.AreEqual(oldTcpTunnelledCount + 1, app.GetSessionStatus().TcpTunnelledCount);
        Assert.AreEqual(oldTcpPassthruCount, app.GetSessionStatus().TcpPassthruCount);

        // text exclude
        oldTcpTunnelledCount = app.GetSessionStatus().TcpTunnelledCount;
        oldTcpPassthruCount = app.GetSessionStatus().TcpPassthruCount;
        await TestHelper.Test_Https(uri: TestConstants.HttpsUri1);
        Assert.AreEqual(oldTcpTunnelledCount, app.GetSessionStatus().TcpTunnelledCount);
        Assert.AreEqual(oldTcpPassthruCount + 1, app.GetSessionStatus().TcpPassthruCount);
    }

    [TestMethod]
    public async Task Premium_token_must_create_premium_session()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        await using var app = TestAppHelper.CreateClientApp();
        var clientProfile = app.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        await app.Connect(clientProfile.ClientProfileId, diagnose: true);


        Assert.IsTrue(app.State.ClientProfile?.IsPremiumAccount);
        Assert.IsTrue(app.State.SessionInfo?.IsPremiumSession);
    }

    [TestMethod]
    public async Task ServerLocation_must_reset_to_default_for_no_server_error()
    {
        // Create Server 1
        using var accessManager = TestHelper.CreateAccessManager(serverLocation: "US/california");
        await using var server = await TestHelper.CreateServer(accessManager);

        // Create Client
        var token = accessManager.CreateToken();
        token.ServerToken.ServerLocations = ["US", "FR/Paris"];

        // Create App
        await using var clientApp = TestAppHelper.CreateClientApp();
        var clientProfile = clientApp.ClientProfileService.ImportAccessKey(token.ToAccessKey());
        clientApp.ClientProfileService.Update(clientProfile.ClientProfileId, new ClientProfileUpdateParams {
            SelectedLocation = "FR/Paris"
        });

        // Connect
        try {
            await clientApp.Connect(clientProfile.ClientProfileId);
            Assert.Fail("SessionException was expected.");
        }
        catch (SessionException ex) {
            Assert.AreEqual(SessionErrorCode.NoServerAvailable, ex.SessionResponse.ErrorCode);
        }

        // reload clientProfile
        clientProfile =  clientApp.ClientProfileService.Get(clientProfile.ClientProfileId);
        Assert.IsTrue(clientProfile.ToInfo().SelectedLocationInfo?.IsAuto);
    }
}