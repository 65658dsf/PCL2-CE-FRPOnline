Imports PCL.Core.App
Imports PCL.Core.Link

Class PageLinkSetup

    Private Shadows IsLoaded As Boolean = False
    Private IsFirstLoad As Boolean = True
    Private _stellarAuth As StellarAuthorizeServer = Nothing
    Private Sub PageSetupLink_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        '非重复加载部分
        If IsLoaded Then Return
        IsLoaded = True

        AniControlEnabled += 1
        Reload()
        AniControlEnabled -= 1

    End Sub
    Public Sub Reload() Handles Me.Loaded
        ' 填充服务商下拉框（防止选择事件重入）
        Try
            AniControlEnabled += 1
            ComboFrpProvider.Items.Clear()
            ComboFrpProvider.Items.Add(New MyComboBoxItem() With {.Tag = "stellar", .Content = "StellarFrp"})
            ComboFrpProvider.Items.Add(New MyComboBoxItem() With {.Tag = "local", .Content = "本地 FRP"})
            Dim cur = Config.Link.FrpProvider
            Dim idx As Integer = 0
            For i = 0 To ComboFrpProvider.Items.Count - 1
                Dim it = CType(ComboFrpProvider.Items(i), MyComboBoxItem)
                If String.Equals(CStr(it.Tag), cur, StringComparison.OrdinalIgnoreCase) Then idx = i : Exit For
            Next
            ComboFrpProvider.SelectedIndex = idx
        Catch
        Finally
            AniControlEnabled -= 1
        End Try

        If String.Equals(Config.Link.FrpProvider, "stellar", StringComparison.OrdinalIgnoreCase) Then
            If String.IsNullOrWhiteSpace(Config.Link.StellarToken) Then
                CardLogged.Visibility = Visibility.Collapsed
                CardNotLogged.Visibility = Visibility.Visible
                BtnLogin.Visibility = Visibility.Visible
                BtnRegister.Visibility = Visibility.Visible
                BtnCancel.Visibility = Visibility.Collapsed
                TextLogin.Text = "登录 StellarFrp 以使用中心转发隧道"
            Else
                CardLogged.Visibility = Visibility.Visible
                CardNotLogged.Visibility = Visibility.Collapsed
                TextUsername.Text = "已登录 StellarFrp"
                TextStatus.Text = "已授权使用 StellarFrp 隧道"
                RefreshStellarNodes()
            End If
            CardLocalFrp.Visibility = Visibility.Collapsed
            CardStellarTunnel.Visibility = Visibility.Visible
        Else
            ' 本地 FRP 选择时，无需登录，隐藏登录卡片
            CardLogged.Visibility = Visibility.Collapsed
            CardNotLogged.Visibility = Visibility.Visible
            TextLogin.Text = "选择本地 FRP 无需登录"
            BtnLogin.Visibility = Visibility.Collapsed
            BtnRegister.Visibility = Visibility.Collapsed
            BtnCancel.Visibility = Visibility.Collapsed
            CardLocalFrp.Visibility = Visibility.Visible
            RefreshLocalFrpUi()
            CardStellarTunnel.Visibility = Visibility.Collapsed
        End If
    End Sub
    
    Private Sub BtnLogin_Click(sender As Object, e As RoutedEventArgs) Handles BtnLogin.Click
        BtnLogin.Visibility = Visibility.Collapsed
        BtnRegister.Visibility = Visibility.Collapsed
        BtnCancel.Visibility = Visibility.Visible
        TextLogin.Text = "请在浏览器中完成授权，然后回到启动器中继续..."
        _stellarAuth = New StellarAuthorizeServer()
        RunInNewThread(Sub()
                           Dim tk = _stellarAuth.StartAuthorizeAsync().GetAwaiter().GetResult()
                           If String.IsNullOrWhiteSpace(tk) Then
                               RunInUi(Sub()
                                           BtnLogin.Visibility = Visibility.Visible
                                           BtnRegister.Visibility = Visibility.Visible
                                           BtnCancel.Visibility = Visibility.Collapsed
                                           TextLogin.Text = "登录 StellarFrp 以使用中心转发隧道"
                                       End Sub)
                               Exit Sub
                           End If
                            RunInUi(Sub()
                                        CardLogged.Visibility = Visibility.Visible
                                        CardNotLogged.Visibility = Visibility.Collapsed
                                        TextUsername.Text = "已登录 StellarFrp"
                                        TextStatus.Text = "已授权使用 StellarFrp 隧道"
                                    End Sub)
                           Try
                               Dim ui = StellarFrpApi.GetUserInfo(Config.Link.StellarToken)
                               If ui IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(ui.Username) Then
                                   Setup.Set("LinkUsername", ui.Username)
                                   Log("[Link] 已同步 StellarFrp 用户名：" & ui.Username)
                               End If
                           Catch ex As Exception
                               Log(ex, "[Link] 同步 StellarFrp 用户名失败")
                           End Try
                           RefreshStellarNodes()
                       End Sub)
    End Sub
    Private Sub BtnCancel_Click(sender As Object, e As RoutedEventArgs) Handles BtnCancel.Click
        BtnLogin.Visibility = Visibility.Visible
        BtnRegister.Visibility = Visibility.Visible
        BtnCancel.Visibility = Visibility.Collapsed
        TextLogin.Text = "登录 StellarFrp 以使用中心转发隧道"
        _stellarAuth?.Cancel()
        Hint("已取消登录！")
    End Sub
    Private Sub BtnLogout_Click(sender As Object, e As RoutedEventArgs) Handles BtnLogout.Click
        If MyMsgBox("你确定要退出登录吗？", "退出登录", "确定", "取消") = 1 Then
            Config.Link.StellarToken = ""
            Config.Link.StellarTokenConfig.Reset()
            BtnLogin.Visibility = Visibility.Visible
            BtnRegister.Visibility = Visibility.Visible
            BtnCancel.Visibility = Visibility.Collapsed
            TextLogin.Text = "登录 StellarFrp 以使用中心转发隧道"
            CardLogged.Visibility = Visibility.Collapsed
            CardNotLogged.Visibility = Visibility.Visible
            Reload()
            Log("[Link] 已退出 StellarFrp")
            Hint("已退出登录！", HintType.Finish, False)
        End If
    End Sub
    Private Sub BtnQuit_Click(sender As Object, e As RoutedEventArgs) Handles BtnQuit.Click
        If MyMsgBox("你确定要撤销联机协议授权吗？", "撤销授权确认", "确定", "取消", IsWarn:=True) = 1 Then
            Config.Link.StellarTokenConfig.Reset()
            Config.Link.LinkEulaConfig.Reset()
            RunInUi(Sub()
                        FrmLinkLeft.PageChange(FormMain.PageSubType.LinkLobby)
                        FrmLinkLeft.ItemLobby.SetChecked(True, False, False)
                        FrmMain.PageChange(New FormMain.PageStackData With {.Page = FormMain.PageType.Launch})
                        FrmLinkLobby = Nothing
                    End Sub)
            Hint("联机功能已停用！")
        End If
    End Sub
    '初始化
    Public Sub Reset()
        Try
            Config.Link.SelectedNodeIdConfig.Reset()
            Log("[Setup] 已初始化 StellarFrp 设置")
            Hint("已初始化 StellarFrp 设置！", HintType.Finish, False)
            Reload()
        Catch ex As Exception
            Log(ex, "初始化 StellarFrp 设置失败", LogLevel.Msgbox)
        End Try
        Reload()
    End Sub

    '将控件改变路由到设置改变
    Private Sub BtnRefreshNodes_Click(sender As Object, e As RoutedEventArgs) Handles BtnRefreshNodes.Click
        RefreshStellarNodes()
    End Sub

    Private Sub ComboStellarNode_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboStellarNode.SelectionChanged
        If AniControlEnabled = 0 AndAlso ComboStellarNode.SelectedItem IsNot Nothing Then
            Dim item = CType(ComboStellarNode.SelectedItem, MyComboBoxItem)
            Dim id = CType(item.Tag, Integer)
            Setup.Set("LinkSelectedNodeId", id)
            TextSelectedNode.Text = id.ToString()
        End If
    End Sub

    Private Sub ComboFrpProvider_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboFrpProvider.SelectionChanged
        If AniControlEnabled = 0 AndAlso ComboFrpProvider.SelectedItem IsNot Nothing Then
            Dim item = CType(ComboFrpProvider.SelectedItem, MyComboBoxItem)
            Dim key = CStr(item.Tag)
            If Not String.Equals(key, Config.Link.FrpProvider, StringComparison.OrdinalIgnoreCase) Then
                Setup.Set("LinkFrpProvider", key)
                Reload()
            End If
        End If
    End Sub

    Private Sub RefreshStellarNodes()
        If String.IsNullOrWhiteSpace(Config.Link.StellarToken) Then Return
        RunInNewThread(Sub()
                           Try
                                   Dim nodes = StellarFrpApi.GetNodesV2(Config.Link.StellarToken)
                               Dim suggestions = nodes.Where(Function(n) n.Status = 1 AndAlso n.AllowedTypes IsNot Nothing AndAlso n.AllowedTypes.Contains("TCP")).OrderByDescending(Function(n) n.Bandwidth).ToList()
                               RunInUi(Sub()
                                           ComboStellarNode.Items.Clear()
                                           For Each n In nodes
                                               Dim item As New MyComboBoxItem() With {.Tag = n.Id, .Content = n.Name}
                                               item.IsEnabled = (n.Status = 1)
                                               ComboStellarNode.Items.Add(item)
                                           Next
                                           Dim selectedId = Config.Link.SelectedNodeId
                                           If selectedId > 0 Then
                                               Dim idx = -1
                                               For i = 0 To ComboStellarNode.Items.Count - 1
                                                   Dim it = CType(ComboStellarNode.Items(i), MyComboBoxItem)
                                                   If CType(it.Tag, Integer) = selectedId AndAlso it.IsEnabled Then idx = i : Exit For
                                               Next
                                               ComboStellarNode.SelectedIndex = idx
                                               TextSelectedNode.Text = selectedId.ToString()
                                           End If
                                       End Sub)
                           Catch ex As Exception
                               Log(ex, "[StellarFrp] 节点列表获取失败", LogLevel.Debug)
                           End Try
                       End Sub)
    End Sub

    Private Sub RefreshLocalFrpUi()
        Try
            Dim profilesJson = Config.Link.LocalFrpProfiles
            Dim selectedId = Config.Link.LocalFrpProfileId
            Dim name As String = "未设置"
            Dim detail As String = "-"
            If String.IsNullOrWhiteSpace(profilesJson) OrElse Not profilesJson.Trim().StartsWith("[") Then
                Dim legacyHost = Setup.Get("FrpsHost")
                Dim legacyPortStr = Setup.Get("FrpsPort")
                Dim legacyPort As Integer = 0
                Integer.TryParse(legacyPortStr, legacyPort)
                If Not String.IsNullOrWhiteSpace(legacyHost) AndAlso legacyPort > 0 Then
                    Dim arrNew As New Newtonsoft.Json.Linq.JArray()
                    Dim id0 = Guid.NewGuid().ToString("N")
                    Dim obj0 As New Newtonsoft.Json.Linq.JObject()
                    obj0("Id") = id0
                    obj0("Name") = "默认配置"
                    obj0("ServerAddr") = legacyHost
                    obj0("ServerPort") = legacyPort
                    obj0("RemoteRange") = "10000-60000"
                    arrNew.Add(obj0)
                    Config.Link.LocalFrpProfiles = arrNew.ToString()
                    Config.Link.LocalFrpProfileId = id0
                    profilesJson = Config.Link.LocalFrpProfiles
                    selectedId = id0
                End If
            End If
            If Not String.IsNullOrWhiteSpace(profilesJson) AndAlso profilesJson.Trim().StartsWith("[") Then
                Dim arr = Newtonsoft.Json.Linq.JArray.Parse(profilesJson)
                Dim found As Newtonsoft.Json.Linq.JObject = Nothing
                If Not String.IsNullOrWhiteSpace(selectedId) Then
                    For Each it As Newtonsoft.Json.Linq.JObject In arr
                        If String.Equals(it.Value(Of String)("Id"), selectedId, StringComparison.OrdinalIgnoreCase) Then found = it : Exit For
                    Next
                End If
                If found Is Nothing AndAlso arr.Count > 0 Then found = CType(arr(0), Newtonsoft.Json.Linq.JObject)
                If found IsNot Nothing Then
                    name = found.Value(Of String)("Name")
                    Dim addr = found.Value(Of String)("ServerAddr")
                    Dim port = found.Value(Of Integer?)("ServerPort")
                    Dim range = found.Value(Of String)("RemoteRange")
                    detail = $"{addr}:{If(port.HasValue, port.Value, 0)} | {range}"
                    Config.Link.LocalFrpProfileId = found.Value(Of String)("Id")
                End If
            End If
            TextLocalFrpProfile.Text = name
            TextLocalFrpDetail.Text = detail
            CheckUseToml.Checked = Config.Link.LocalUseToml
        Catch
        End Try
    End Sub

    Private Sub BtnAddLocalFrp_Click(sender As Object, e As RoutedEventArgs) Handles BtnAddLocalFrp.Click
        PanelAddLocalFrp.Visibility = Visibility.Visible
        InputLocalFrpName.Text = ""
        InputLocalFrpAddr.Text = ""
        InputLocalFrpPort.Text = ""
        InputLocalFrpRange.Text = "40000-50000"
    End Sub

    Private Sub BtnSaveLocalFrp_Click(sender As Object, e As RoutedEventArgs) Handles BtnSaveLocalFrp.Click
        Dim name = InputLocalFrpName.Text?.Trim()
        Dim addr = InputLocalFrpAddr.Text?.Trim()
        Dim portStr = InputLocalFrpPort.Text?.Trim()
        Dim range = InputLocalFrpRange.Text?.Trim()
        Dim port As Integer = 0
        Integer.TryParse(portStr, port)
        If String.IsNullOrWhiteSpace(name) OrElse String.IsNullOrWhiteSpace(addr) OrElse port <= 0 OrElse String.IsNullOrWhiteSpace(range) OrElse Not range.Contains("-") Then
            Hint("请完整填写配置名称、地址、端口与端口范围（形如 40000-50000）", HintType.Info)
            Return
        End If

        Try
            Dim arr As Newtonsoft.Json.Linq.JArray
            If String.IsNullOrWhiteSpace(Config.Link.LocalFrpProfiles) OrElse Not Config.Link.LocalFrpProfiles.Trim().StartsWith("[") Then
                arr = New Newtonsoft.Json.Linq.JArray()
            Else
                arr = Newtonsoft.Json.Linq.JArray.Parse(Config.Link.LocalFrpProfiles)
            End If
            Dim id = Guid.NewGuid().ToString("N")
            Dim obj As New Newtonsoft.Json.Linq.JObject()
            obj("Id") = id
            obj("Name") = name
            obj("ServerAddr") = addr
            obj("ServerPort") = port
            obj("RemoteRange") = range
            arr.Add(obj)
            Config.Link.LocalFrpProfiles = arr.ToString()
            Config.Link.LocalFrpProfileId = id
            Config.Link.LocalUseToml = True
            PanelAddLocalFrp.Visibility = Visibility.Collapsed
            RefreshLocalFrpUi()
            Hint("已保存本地 FRP 配置", HintType.Finish, False)
        Catch ex As Exception
            Log(ex, "保存本地 FRP 配置失败", LogLevel.Hint)
        End Try
    End Sub

    Private Sub CheckUseToml_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles CheckUseToml.MouseLeftButtonUp
        Try
            Config.Link.LocalUseToml = CheckUseToml.Checked
        Catch
        End Try
    End Sub

End Class
