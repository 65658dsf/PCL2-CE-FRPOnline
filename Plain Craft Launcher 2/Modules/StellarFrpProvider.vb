Imports PCL.Core.App
Imports PCL.Core.IO
Imports Newtonsoft.Json.Linq
Imports System.Net
Imports System.Net.Sockets
Imports System.Web

Public Interface IFrpProvider
    Function AuthenticateAsync() As Task(Of Boolean)
    Function GetSuggestedNodesAsync() As Task(Of List(Of ProviderNode))
    Function CreateOrStartTunnelAsync(localPort As Integer, username As String, Optional lobbyCode As String = "") As Task(Of Tuple(Of Boolean, String))
    Function StopTunnelAsync() As Task
    Function DeleteTunnelIfNeededAsync() As Task
    Function GetPublicAddress() As String
End Interface

Public Class ProviderNode
    Public Property Id As Integer
    Public Property Name As String
    Public Property Region As String
End Class

Public Module FrpProviderFactory
    Public Function GetProvider(name As String) As IFrpProvider
        Dim key = If(String.IsNullOrWhiteSpace(name), "local", name.ToLowerInvariant())
        Select Case key
            Case "stellar"
                Return New StellarFrpProvider()
            Case Else
                Return New LocalFrpProvider()
        End Select
    End Function
End Module

Public Class StellarFrpProvider
    Implements IFrpProvider

    Private _currentPublicAddress As String = Nothing

    Public Async Function AuthenticateAsync() As Task(Of Boolean) Implements IFrpProvider.AuthenticateAsync
        Await Task.Yield()
        Try
            Dim token = Config.Link.StellarToken
            If Not String.IsNullOrWhiteSpace(token) Then Return True
            Dim auth As New StellarAuthorizeServer()
            token = Await auth.StartAuthorizeAsync()
            If String.IsNullOrWhiteSpace(token) Then Return False
            Config.Link.StellarToken = token
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Async Function GetSuggestedNodesAsync() As Task(Of List(Of ProviderNode)) Implements IFrpProvider.GetSuggestedNodesAsync
        Await Task.Yield()
        Dim result As New List(Of ProviderNode)()
        Try
            Dim token = Config.Link.StellarToken
            If String.IsNullOrWhiteSpace(token) Then Return result
            Dim nodes = StellarFrpApi.GetNodesV2(token)
            Dim suggestions = nodes.Where(Function(n) n.Status = 1 AndAlso n.AllowedTypes IsNot Nothing AndAlso n.AllowedTypes.Contains("TCP")).ToList()
            For Each n In suggestions
                result.Add(New ProviderNode() With {.Id = n.Id, .Name = n.Name, .Region = n.Region})
            Next
        Catch
        End Try
        Return result
    End Function

    Public Async Function CreateOrStartTunnelAsync(localPort As Integer, username As String, Optional lobbyCode As String = "") As Task(Of Tuple(Of Boolean, String)) Implements IFrpProvider.CreateOrStartTunnelAsync
        Await Task.Yield()
        Try
            Dim res = Await StellarFrpInternals.StartCreateTunnelAsync(localPort, username, lobbyCode)
            If res.Item1 Then _currentPublicAddress = res.Item2
            Return res
        Catch ex As Exception
            Return New Tuple(Of Boolean, String)(False, ex.Message)
        End Try
    End Function

    Public Async Function StopTunnelAsync() As Task Implements IFrpProvider.StopTunnelAsync
        Await FrpController.StopAsync()
    End Function

    Public Async Function DeleteTunnelIfNeededAsync() As Task Implements IFrpProvider.DeleteTunnelIfNeededAsync
        Await Task.Yield()
        Try
            Dim tid = Config.Link.CurrentTunnelId
            Dim token = Config.Link.StellarToken
            If tid > 0 AndAlso Not String.IsNullOrWhiteSpace(token) Then
                Dim ok = StellarFrpApi.DeleteProxy(token, tid)
                If ok Then Config.Link.CurrentTunnelId = 0
            End If
        Catch
        End Try
    End Function

    Public Function GetPublicAddress() As String Implements IFrpProvider.GetPublicAddress
        Dim addr As String = _currentPublicAddress
        If String.IsNullOrWhiteSpace(addr) AndAlso Not String.IsNullOrWhiteSpace(FrpController.PublicHost) AndAlso FrpController.PublicPort > 0 Then
            addr = FrpController.PublicHost & ":" & FrpController.PublicPort.ToString()
        End If
        Return addr
    End Function
End Class

Public Module StellarFrpInternals
    Private Function ParsePortRange(range As String) As Tuple(Of Integer, Integer)
        If String.IsNullOrWhiteSpace(range) Then Return New Tuple(Of Integer, Integer)(10000, 60000)
        Dim parts = range.Split("-"c)
        If parts.Length <> 2 Then Return New Tuple(Of Integer, Integer)(10000, 60000)
        Dim minV As Integer = 10000, maxV As Integer = 60000
        Integer.TryParse(parts(0), minV)
        Integer.TryParse(parts(1), maxV)
        Return New Tuple(Of Integer, Integer)(minV, maxV)
    End Function

Public Async Function StartCreateTunnelAsync(localPort As Integer, username As String, Optional lobbyCode As String = "") As Task(Of Tuple(Of Boolean, String))
        Await Task.Yield()
        Try
            If Not ModLink.IsStellarFrpcInstalled() Then
                ModLink.StellarFrpDownloadClient()
                Dim waitDeadline = DateTime.UtcNow.AddSeconds(60)
                While DateTime.UtcNow < waitDeadline AndAlso Not ModLink.IsStellarFrpcInstalled()
                    Await Task.Delay(1000)
                End While
                If Not ModLink.IsStellarFrpcInstalled() Then
                    Return New Tuple(Of Boolean, String)(False, "依赖下载失败")
                End If
            End If

            Dim token = Config.Link.StellarToken
            If String.IsNullOrWhiteSpace(token) Then
                Dim auth As New StellarAuthorizeServer()
                token = Await auth.StartAuthorizeAsync()
                If String.IsNullOrWhiteSpace(token) Then
                    Return New Tuple(Of Boolean, String)(False, "授权失败或已取消")
                End If
                Config.Link.StellarToken = token
            End If

            Dim nodes = StellarFrpApi.GetNodesV2(token)
            Dim selectedId = Config.Link.SelectedNodeId
            Dim candidate = If(selectedId > 0, nodes.FirstOrDefault(Function(n) n.Id = selectedId), nodes.FirstOrDefault(Function(n) n.Status = 1 AndAlso n.AllowedTypes IsNot Nothing AndAlso n.AllowedTypes.Contains("TCP")))
            If candidate Is Nothing Then
                Return New Tuple(Of Boolean, String)(False, "未找到可用节点")
            End If
            Setup.Set("LinkSelectedNodeId", candidate.Id)

            Dim pr = ParsePortRange(candidate.PortRange)
            Dim rnd As New Random()
            Dim remotePort As Integer = rnd.Next(pr.Item1, pr.Item2)

            Dim pattern = Config.Link.ProxyNamePattern
            If String.IsNullOrWhiteSpace(pattern) Then pattern = "pclce-{LobbyCode}-{Username}"
            Dim proxyName = pattern.Replace("{Username}", username).Replace("{LobbyCode}", lobbyCode)

            Dim payload As New JObject(
            New JProperty("nodeId", candidate.Id),
            New JProperty("proxyName", proxyName),
            New JProperty("proxyType", "tcp"),
            New JProperty("localIp", "127.0.0.1"),
            New JProperty("localPort", localPort),
            New JProperty("remotePort", remotePort),
            New JProperty("domain", ""),
            New JProperty("hostHeaderRewrite", ""),
            New JProperty("headerXFromWhere", ""),
            New JProperty("proxyProtocolVersion", ""),
            New JProperty("useEncryption", True),
            New JProperty("useCompression", True),
            New JProperty("autoTls", False),
            New JProperty("crtBase64", ""),
            New JProperty("keyBase64", "")
        )

            Dim id As Integer = 0
            Try
                Dim existsList = StellarFrpApi.GetProxyList(token, page:=1, pageSize:=50)
                If existsList IsNot Nothing Then
                    For Each item As JObject In existsList
                        Dim pn = If(item("ProxyName") IsNot Nothing, item("ProxyName").ToString(), item("proxyName")?.ToString())
                        If pn = proxyName Then
                            id = Val(If(item("Id") IsNot Nothing, item("Id").ToString(), item("id")?.ToString()))
                            Exit For
                        End If
                    Next
                End If
            Catch
            End Try
            If id = 0 Then id = StellarFrpApi.CreateProxy(token, payload)
            If id <= 0 Then
                Return New Tuple(Of Boolean, String)(False, "创建隧道失败")
            End If
            Config.Link.CurrentTunnelId = id

            Dim detail As StellarFrpApi.ProxyDetail = Nothing
            Try
                detail = StellarFrpApi.GetProxyDetail(token, id)
            Catch
                detail = New StellarFrpApi.ProxyDetail()
            End Try
            If String.IsNullOrWhiteSpace(detail.Link) Then
                Try
                    Dim linkTuple = StellarFrpApi.GetProxyLink(token, id)
                    If Not String.IsNullOrWhiteSpace(linkTuple.Item1) Then detail.Link = linkTuple.Item1
                    If detail.RemotePort <= 0 AndAlso linkTuple.Item2 > 0 Then detail.RemotePort = linkTuple.Item2
                Catch
                End Try
            End If
            Dim remoteToUse As Integer = If(detail.RemotePort > 0, detail.RemotePort, remotePort)
            Dim hostFromLink As String = ""
            Dim portFromLink As Integer = 0
            If Not String.IsNullOrWhiteSpace(detail.Link) Then
                Dim idx As Integer = detail.Link.LastIndexOf(":"c)
                If idx > 0 AndAlso idx < detail.Link.Length - 1 Then
                    hostFromLink = detail.Link.Substring(0, idx)
                    Integer.TryParse(detail.Link.Substring(idx + 1), portFromLink)
                    If portFromLink > 0 Then remoteToUse = portFromLink
                End If
            End If
            Dim hostFromToml As String = ""
            If String.IsNullOrWhiteSpace(hostFromLink) AndAlso Not String.IsNullOrWhiteSpace(detail.ConfigToml) Then
                Try
                    Dim m = System.Text.RegularExpressions.Regex.Match(detail.ConfigToml, "serverAddr\s*=\s*""([^""]+)""")
                    If m.Success Then hostFromToml = m.Groups(1).Value
                Catch
                End Try
            End If
            Dim ok As Boolean
            Dim hostForStart As String = If(Not String.IsNullOrWhiteSpace(hostFromLink), hostFromLink, If(Not String.IsNullOrWhiteSpace(hostFromToml), hostFromToml, detail.ServerAddr))
            ok = Await FrpController.StartWithStellarAsync(hostForStart, remoteToUse, token, id)
            If Not ok AndAlso Not String.IsNullOrWhiteSpace(detail.ServerAddr) AndAlso detail.ServerPort > 0 AndAlso Not String.IsNullOrWhiteSpace(detail.ConfigToml) Then
                ok = Await FrpController.StartWithServerAsync(detail.ServerAddr, detail.ServerPort, remoteToUse, localPort, proxyName, detail.ConfigToml)
            End If
            If ok Then
                Dim host = If(String.IsNullOrWhiteSpace(FrpController.PublicHost), If(Not String.IsNullOrWhiteSpace(hostFromLink), hostFromLink, If(Not String.IsNullOrWhiteSpace(hostFromToml), hostFromToml, detail.ServerAddr)), FrpController.PublicHost)
                Dim addr = If(Not String.IsNullOrWhiteSpace(detail.Link), detail.Link, host & ":" & If(FrpController.PublicPort > 0, FrpController.PublicPort, remoteToUse))
                If String.IsNullOrWhiteSpace(addr) OrElse Not addr.Contains(":") OrElse addr.StartsWith(":") Then
                    Dim fallbackHost = If(Not String.IsNullOrWhiteSpace(hostForStart), hostForStart, If(Not String.IsNullOrWhiteSpace(FrpController.PublicHost), FrpController.PublicHost, ""))
                    addr = fallbackHost & ":" & remoteToUse
                End If
                Return New Tuple(Of Boolean, String)(True, addr)
            End If
            Return New Tuple(Of Boolean, String)(False, "隧道启动失败")
        Catch ex As Exception
            Return New Tuple(Of Boolean, String)(False, ex.Message)
        End Try
    End Function
End Module

Public Class StellarAuthorizeServer
    Private _listener As HttpListener
    Private _cts As CancellationTokenSource

    Private Shared Function GetFreePort() As Integer
        Dim l As New TcpListener(IPAddress.Loopback, 0)
        l.Start()
        Dim p = CType(l.LocalEndpoint, IPEndPoint).Port
        l.Stop()
        Return p
    End Function

    Public Async Function StartAuthorizeAsync() As Task(Of String)
        _cts = New CancellationTokenSource()
        Dim tcs As New TaskCompletionSource(Of String)()
        Dim state = Guid.NewGuid().ToString("N")

        Dim url = $"http://console.stellarfrp.top/Authorize?client_id=stellarfrp-mc&redirect_uri=%r&state={state}"
        Dim started = ModWebServer.StartOAuthWaitingCallback("StellarFrp", url,
            Function(success As Boolean, parameters As IDictionary(Of String, String), content As String)
                If Not success Then
                    tcs.TrySetResult(Nothing)
                    Return ModWebServer.OAuthCompleteStatus.Failed(content)
                End If
                Try
                    Dim qState = If(parameters.ContainsKey("state"), parameters("state"), Nothing)
                    Dim qType = If(parameters.ContainsKey("type"), parameters("type"), Nothing)
                    Dim qToken = If(parameters.ContainsKey("token"), parameters("token"), Nothing)
                    If (qState Is Nothing OrElse qState = state) AndAlso qType = "allowed" AndAlso Not String.IsNullOrWhiteSpace(qToken) Then
                        Config.Link.StellarToken = qToken
                        tcs.TrySetResult(qToken)
                        Return ModWebServer.OAuthCompleteStatus.Complete("ok")
                    Else
                        tcs.TrySetResult(Nothing)
                        Return ModWebServer.OAuthCompleteStatus.Failed("授权失败或回调参数无效")
                    End If
                Catch ex As Exception
                    tcs.TrySetResult(Nothing)
                    Return ModWebServer.OAuthCompleteStatus.Failed("处理授权回调失败", ex)
                End Try
            End Function, redirectTemplate:=$"http://127.0.0.1:{"{port}"}/static/?callback")

        If Not started Then
            tcs.TrySetResult(Nothing)
        End If

        Dim token = Await tcs.Task.ConfigureAwait(False)
        Return token
    End Function

    Public Sub Cancel()
        Try
            _cts?.Cancel()
            _listener?.Close()
        Catch
        End Try
    End Sub
End Class

Public Class StellarFrpApi
    Private Shared ReadOnly BaseUrl As String = "https://api.stellarfrp.top"

    Public Class StellarNode
        Public Property Id As Integer
        Public Property Name As String
        Public Property Region As String
        Public Property Bandwidth As Integer
        Public Property Status As Integer
        Public Property AllowedTypes As List(Of String)
        Public Property PortRange As String
    End Class

    Public Class ProxyDetail
        Public Property ServerAddr As String
        Public Property ServerPort As Integer
        Public Property RemotePort As Integer
        Public Property ProxyName As String
        Public Property LocalPort As Integer
        Public Property NodeId As Integer
        Public Property ConfigToml As String
        Public Property Link As String
    End Class

    Public Class UserInfo
        Public Property Username As String
        Public Property Email As String
        Public Property Status As Integer
        Public Property GroupName As String
    End Class

    Private Shared Function AuthHeaders(token As String) As Dictionary(Of String, String)
        Dim headers As New Dictionary(Of String, String)
        If Not String.IsNullOrWhiteSpace(token) Then headers("Authorization") = token
        headers("Accept") = "application/json"
        Return headers
    End Function

    Public Shared Function GetNodesV2(token As String) As List(Of StellarNode)
        Dim url = BaseUrl & "/api/v1/nodes/get"
        Dim res = ModNet.NetRequestOnce(url, "GET", Nothing, "application/json", Headers:=AuthHeaders(token))
        Dim j As JObject = GetJson(res)
        Dim nodes As New List(Of StellarNode)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Dim data = TryCast(j("data"), JObject)
        If data IsNot Nothing Then
            For Each prop In data.Properties()
                Dim obj = CType(prop.Value, JObject)
                Dim n As New StellarNode()
                Dim nodeName = obj("NodeName")?.ToString()
                n.Id = If(obj("ID") IsNot Nothing, Val(obj("ID")), Val(prop.Name))
                n.Name = nodeName
                n.Status = Val(obj("Status"))
                n.AllowedTypes = (CType(obj("AllowedTypes"), JArray))?.Select(Function(x) x.ToString()).ToList()
                n.PortRange = obj("PortRange")?.ToString()
                nodes.Add(n)
            Next
        End If
        Return nodes
    End Function

    Public Shared Function GetProxyList(token As String, Optional page As Integer = 1, Optional pageSize As Integer = 10) As JArray
        Dim url = BaseUrl & $"/api/v1/proxy/get?page={page}&page_size={pageSize}"
        Dim res = ModNet.NetRequestOnce(url, "GET", Nothing, "application/json", Headers:=AuthHeaders(token))
        Dim j As JObject = GetJson(res)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Return CType(j("data"), JArray)
    End Function

    Public Shared Function GetProxyDetail(token As String, id As Integer) As ProxyDetail
        Dim url = BaseUrl & "/api/v1/proxy/get"
        Dim body As String = New JObject(New JProperty("id", id)).ToString()
        Log("[StellarFrp][Detail] url=" & url & " body=" & body)
        Dim res = ModNet.NetRequestOnce(url, "POST", body, "application/json", Headers:=AuthHeaders(token))
        Log("[StellarFrp][Detail] response=" & res)
        Dim j As JObject = GetJson(res)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Dim data = CType(j("data"), JObject)
        Dim detail As New ProxyDetail()

        detail.ServerAddr = data("serverAddr")?.ToString()
        detail.ServerPort = Val(data("serverPort"))
        detail.RemotePort = Val(data("remotePort"))
        detail.ProxyName = data("ProxyName")?.ToString()
        detail.LocalPort = Val(data("LocalPort"))
        detail.NodeId = Val(data("NodeId"))

        Dim tunnel = CType(data("tunnel"), JObject)
        If Not IsNothing(tunnel) Then
            For Each p In tunnel.Properties()
                Dim seg = CType(p.Value, JObject)
                If Not IsNothing(seg("data")) Then
                    detail.ConfigToml = seg("data").ToString()
                    Exit For
                End If
            Next
        End If

        Dim topTunnel As JObject = Nothing
        If Not IsNothing(data) Then topTunnel = TryCast(data("tunnel"), JObject)
        If IsNothing(topTunnel) Then topTunnel = TryCast(j("tunnel"), JObject)

        If Not IsNothing(CType(topTunnel, Object)) Then
            For Each p In topTunnel.Properties()
                Dim seg = TryCast(p.Value, JObject)
                If IsNothing(seg) Then Continue For
                If String.IsNullOrWhiteSpace(detail.ConfigToml) AndAlso seg.ContainsKey("data") Then
                    detail.ConfigToml = seg("data").ToString()
                End If
                If String.IsNullOrWhiteSpace(detail.Link) AndAlso seg.ContainsKey("Link") Then
                    detail.Link = seg("Link").ToString()
                End If
                If detail.RemotePort <= 0 AndAlso seg.ContainsKey("RemotePort") Then
                    Dim rp As Integer = 0
                    Integer.TryParse(seg("RemotePort").ToString(), rp)
                    If rp > 0 Then detail.RemotePort = rp
                End If
                If detail.LocalPort <= 0 AndAlso seg.ContainsKey("LocalPort") Then
                    Dim lp As Integer = 0
                    Integer.TryParse(seg("LocalPort").ToString(), lp)
                    If lp > 0 Then detail.LocalPort = lp
                End If
                If String.IsNullOrWhiteSpace(detail.ProxyName) AndAlso seg.ContainsKey("ProxyName") Then
                    detail.ProxyName = seg("ProxyName").ToString()
                End If
                If detail.NodeId <= 0 AndAlso seg.ContainsKey("NodeId") Then
                    Dim nid As Integer = 0
                    Integer.TryParse(seg("NodeId").ToString(), nid)
                    If nid > 0 Then detail.NodeId = nid
                End If
                Exit For
            Next
        End If

        If (String.IsNullOrWhiteSpace(detail.ServerAddr) OrElse detail.ServerPort <= 0) AndAlso Not String.IsNullOrWhiteSpace(detail.ConfigToml) Then
            Try
                Dim m1 = System.Text.RegularExpressions.Regex.Match(detail.ConfigToml, "serverAddr\s*=\s*""([^""]+)""")
                If m1.Success Then detail.ServerAddr = m1.Groups(1).Value
                Dim m2 = System.Text.RegularExpressions.Regex.Match(detail.ConfigToml, "serverPort\s*=\s*([0-9]+)")
                If m2.Success Then
                    Dim sp As Integer = 0
                    Integer.TryParse(m2.Groups(1).Value, sp)
                    If sp > 0 Then detail.ServerPort = sp
                End If
            Catch
            End Try
        End If

        Dim tObj = TryCast(data("tunnel"), JObject)
        If Not IsNothing(tObj) Then
            For Each p In tObj.Properties()
                Dim seg = TryCast(p.Value, JObject)
                If Not IsNothing(seg) Then
                    Try
                        detail.Link = seg("Link").ToString()
                        If Not String.IsNullOrWhiteSpace(detail.Link) Then Exit For
                    Catch
                    End Try
                End If
            Next
        End If
        Return detail
    End Function

    Public Shared Function GetProxyLink(token As String, id As Integer) As Tuple(Of String, Integer)
        Dim url = BaseUrl & "/api/v1/proxy/get"
        Dim body As String = New JObject(New JProperty("id", id)).ToString()
        Dim res = ModNet.NetRequestOnce(url, "POST", body, "application/json", Headers:=AuthHeaders(token))
        Dim j As JObject = GetJson(res)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Dim hostPort As String = ""
        Dim port As Integer = 0
        Dim topTunnel = TryCast(j("tunnel"), JObject)
        If Not IsNothing(topTunnel) Then
            For Each p In topTunnel.Properties()
                Dim seg = TryCast(p.Value, JObject)
                If seg Is Nothing Then Continue For
                If seg.ContainsKey("Link") Then hostPort = seg("Link").ToString()
                If seg.ContainsKey("RemotePort") Then Integer.TryParse(seg("RemotePort").ToString(), port)
                Exit For
            Next
        End If
        Return New Tuple(Of String, Integer)(hostPort, port)
    End Function

    Public Shared Function GetUserInfo(token As String) As UserInfo
        Dim url = BaseUrl & "/api/v1/users/info"
        Dim res = ModNet.NetRequestOnce(url, "GET", Nothing, "application/json", Headers:=AuthHeaders(token))
        Dim j As JObject = GetJson(res)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Dim d = CType(j("data"), JObject)
        If IsNothing(d) Then Return Nothing
        Dim ui As New UserInfo()
        ui.Username = d("Username")?.ToString()
        ui.Email = d("Email")?.ToString()
        ui.Status = Val(d("Status"))
        ui.GroupName = d("GroupName")?.ToString()
        Return ui
    End Function

    Public Shared Function CreateProxy(token As String, payload As JObject) As Integer
        Dim tryUrls = New List(Of String) From {
            BaseUrl & "/api/v1/proxy/create"
        }
        For Each url In tryUrls
            Try
                Log("[StellarFrp][Create] url=" & url & " payload=" & payload.ToString())
                Dim res = ModNet.NetRequestOnce(url, "POST", payload.ToString(), "application/json", Headers:=AuthHeaders(token))
                Log("[StellarFrp][Create] response=" & res)
                Dim j As JObject = GetJson(res)
                Dim codeStr = j("code")?.ToString()
                If codeStr = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
                If codeStr = "400" AndAlso (j("msg")?.ToString()?.Contains("同名隧道") = True) Then
                    Dim pn = payload("proxyName")?.ToString()
                    If Not String.IsNullOrWhiteSpace(pn) Then
                        Try
                            Dim page As Integer = 1
                            Dim pageSize As Integer = 200
                            While True
                                Dim list = GetProxyList(token, page, pageSize)
                                If list Is Nothing OrElse list.Count = 0 Then Exit While
                                For Each item As JObject In list
                                    Dim name As String = Nothing
                                    If item.ContainsKey("ProxyName") Then
                                        name = item("ProxyName").ToString()
                                    ElseIf item.ContainsKey("proxyName") Then
                                        name = item("proxyName").ToString()
                                    End If
                                    If name = pn Then
                                        Dim idStr0 As String = Nothing
                                        If item.ContainsKey("Id") Then
                                            idStr0 = item("Id").ToString()
                                        ElseIf item.ContainsKey("id") Then
                                            idStr0 = item("id").ToString()
                                        End If
                                        Dim id0 As Integer = 0
                                        If Not String.IsNullOrWhiteSpace(idStr0) Then Integer.TryParse(idStr0, id0)
                                        If id0 > 0 Then Return id0
                                    End If
                                Next
                                page += 1
                                If list.Count < pageSize Then Exit While
                            End While
                        Catch
                        End Try
                    End If
                End If
                Dim d = CType(j("data"), JObject)
                If d IsNot Nothing Then
                    Dim idStr = d("Id")?.ToString()
                    If String.IsNullOrWhiteSpace(idStr) Then idStr = d("id")?.ToString()
                    Dim id As Integer = 0
                    Integer.TryParse(idStr, id)
                    If id > 0 Then Return id
                End If
            Catch
            End Try
        Next
        Return 0
    End Function

    Public Shared Function DeleteProxy(token As String, id As Integer) As Boolean
        Dim url = BaseUrl & "/api/v1/proxy/delete"
        Dim body As String = New JObject(New JProperty("id", id)).ToString()
        Dim res = ModNet.NetRequestOnce(url, "POST", body, "application/json", Headers:=AuthHeaders(token))
        Dim j As JObject = GetJson(res)
        If j("code")?.ToString() = "401" Then Throw New Exception("未授权，请先登录 StellarFrp")
        Return j("code")?.ToString() = "200"
    End Function
End Class

Public Class LocalFrpProvider
    Implements IFrpProvider

    Public Async Function AuthenticateAsync() As Task(Of Boolean) Implements IFrpProvider.AuthenticateAsync
        Await Task.Yield()
        Return True
    End Function

    Public Async Function GetSuggestedNodesAsync() As Task(Of List(Of ProviderNode)) Implements IFrpProvider.GetSuggestedNodesAsync
        Await Task.Yield()
        Return New List(Of ProviderNode)()
    End Function

    Public Async Function CreateOrStartTunnelAsync(localPort As Integer, username As String, Optional lobbyCode As String = "") As Task(Of Tuple(Of Boolean, String)) Implements IFrpProvider.CreateOrStartTunnelAsync
        Await Task.Yield()
        Try
            If Not Config.Link.LocalUseToml Then
                Dim okIni = Await FrpController.StartAsync(localPort, username, lobbyCode)
                If okIni Then
                    Dim addrIni = FrpController.PublicHost & ":" & FrpController.PublicPort.ToString()
                    Return New Tuple(Of Boolean, String)(True, addrIni)
                End If
            End If
            Dim profilesJson = Config.Link.LocalFrpProfiles
            Dim selectedId = Config.Link.LocalFrpProfileId
            If String.IsNullOrWhiteSpace(profilesJson) OrElse Not profilesJson.Trim().StartsWith("[") Then
                Return New Tuple(Of Boolean, String)(False, "未配置本地 FRP")
            End If
            Dim arr = Newtonsoft.Json.Linq.JArray.Parse(profilesJson)
            Dim found As Newtonsoft.Json.Linq.JObject = Nothing
            For Each it As Newtonsoft.Json.Linq.JObject In arr
                If String.Equals(it.Value(Of String)("Id"), selectedId, StringComparison.OrdinalIgnoreCase) Then found = it : Exit For
            Next
            If found Is Nothing AndAlso arr.Count > 0 Then found = CType(arr(0), Newtonsoft.Json.Linq.JObject)
            If found Is Nothing Then Return New Tuple(Of Boolean, String)(False, "未配置本地 FRP")

            Dim serverAddr = found.Value(Of String)("ServerAddr")
            Dim serverPort = found.Value(Of Integer?)("ServerPort")
            Dim range = found.Value(Of String)("RemoteRange")
            If String.IsNullOrWhiteSpace(serverAddr) OrElse Not serverPort.HasValue OrElse serverPort.Value <= 0 Then
                Return New Tuple(Of Boolean, String)(False, "本地 FRP 配置不完整")
            End If

            Dim minV As Integer = 10000, maxV As Integer = 60000
            Try
                Dim parts = range?.Split("-"c)
                If parts IsNot Nothing AndAlso parts.Length = 2 Then
                    Integer.TryParse(parts(0), minV)
                    Integer.TryParse(parts(1), maxV)
                End If
            Catch
            End Try
            If minV < 1 OrElse maxV <= minV Then
                minV = 10000 : maxV = 60000
            End If

            Dim pattern = Config.Link.ProxyNamePattern
            If String.IsNullOrWhiteSpace(pattern) Then pattern = "pclce-{LobbyCode}-{Username}"
            Dim proxyName = pattern.Replace("{Username}", username).Replace("{LobbyCode}", lobbyCode)

            Dim tomlDir = IO.Path.Combine(FileService.LocalDataPath, "frp")
            IO.Directory.CreateDirectory(tomlDir)
            Dim tomlPath = IO.Path.Combine(tomlDir, "local.toml")

            Dim rnd As New Random()
            Dim tries As Integer = 0
            Dim lastErr As String = Nothing
            While tries < 10
                Dim remotePort As Integer = rnd.Next(minV, maxV)
                Try
                    Dim sb As New System.Text.StringBuilder()
                    sb.AppendLine("serverAddr = """ & serverAddr & """")
                    sb.AppendLine("serverPort = " & serverPort.Value.ToString())
                    sb.AppendLine()
                    sb.AppendLine("[[proxies]]")
                    sb.AppendLine("name = """ & proxyName & """")
                    sb.AppendLine("type = ""tcp""")
                    sb.AppendLine("localIP = ""127.0.0.1""")
                    sb.AppendLine("localPort = " & localPort.ToString())
                    sb.AppendLine("remotePort = " & remotePort.ToString())
                    IO.File.WriteAllText(tomlPath, sb.ToString(), System.Text.Encoding.UTF8)
                    Dim ok = Await FrpController.StartWithTomlFileAsync(serverAddr, remotePort, tomlPath)
                    If ok Then
                        Dim addr = serverAddr & ":" & remotePort.ToString()
                        Return New Tuple(Of Boolean, String)(True, addr)
                    End If
                Catch ex0 As Exception
                    lastErr = ex0.Message
                End Try
                tries += 1
            End While
            Return New Tuple(Of Boolean, String)(False, If(String.IsNullOrWhiteSpace(lastErr), "隧道启动失败", lastErr))
        Catch ex As Exception
            Return New Tuple(Of Boolean, String)(False, ex.Message)
        End Try
    End Function

    Public Async Function StopTunnelAsync() As Task Implements IFrpProvider.StopTunnelAsync
        Await FrpController.StopAsync()
    End Function

    Public Async Function DeleteTunnelIfNeededAsync() As Task Implements IFrpProvider.DeleteTunnelIfNeededAsync
        Await Task.Yield()
    End Function

    Public Function GetPublicAddress() As String Implements IFrpProvider.GetPublicAddress
        If Not String.IsNullOrWhiteSpace(FrpController.PublicHost) AndAlso FrpController.PublicPort > 0 Then
            Return FrpController.PublicHost & ":" & FrpController.PublicPort.ToString()
        End If
        Return Nothing
    End Function
End Class
