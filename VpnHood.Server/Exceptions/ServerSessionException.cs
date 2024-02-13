﻿using Microsoft.Extensions.Logging;
using System.Net;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Server.Exceptions;

public class ServerSessionException : SessionException, ISelfLog
{
    public IPEndPoint RemoteEndPoint { get; }
    public string? TokenId { get; }
    public ulong? SessionId { get; set; }
    public Session? Session { get; }
    public string RequestId { get; }
    public Guid? ClientId { get; }

    public ServerSessionException(
        IPEndPoint remoteEndPoint,
        Session session,
        SessionErrorCode sessionErrorCode,
        string requestId,
        string message)
        : base(sessionErrorCode, message)
    {
        RemoteEndPoint = remoteEndPoint;
        SessionId = session.SessionId;
        Session = session;
        RequestId = requestId;
    }

    public ServerSessionException(
        IPEndPoint remoteEndPoint,
        Session session,
        SessionResponse sessionResponse,
        string requestId
        )
        : base(sessionResponse)
    {
        RemoteEndPoint = remoteEndPoint;
        SessionId = session.SessionId;
        Session = session;
        RequestId = requestId;
    }

    public ServerSessionException(
        IPEndPoint remoteEndPoint,
        SessionResponse sessionResponse,
        SessionRequest sessionRequest)
    : base(sessionResponse)
    {
        RemoteEndPoint = remoteEndPoint;
        TokenId = sessionRequest.TokenId;
        ClientId = sessionRequest.ClientInfo.ClientId;
        RequestId = sessionRequest.RequestId;
    }

    public ServerSessionException(
        IPEndPoint remoteEndPoint,
        SessionResponse sessionResponse,
        RequestBase requestBase)
        : base(sessionResponse)
    {
        RemoteEndPoint = remoteEndPoint;
        SessionId = requestBase.SessionId;
        RequestId = requestBase.RequestId;
    }

    protected virtual LogLevel LogLevel => LogLevel.Information;
    protected virtual EventId EventId => SessionResponse.ErrorCode is SessionErrorCode.GeneralError
        ? GeneralEventId.Tcp
        : GeneralEventId.Session;

    public virtual void Log()
    {
        VhLogger.Instance.Log(LogLevel, EventId, this,
            "{Message} SessionId: {SessionId}, RequestId: {RequestId}, ClientIp: {ClientIp}, TokenId: {TokenId}, SessionErrorCode: {SessionErrorCode}",
            Message, VhLogger.FormatSessionId(SessionId), RequestId, VhLogger.Format(RemoteEndPoint.Address),
            VhLogger.FormatId(TokenId), SessionResponse.ErrorCode);
    }
}