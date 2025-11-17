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
        If String.IsNullOrWhiteSpace(Config.Link.StellarToken) Then
            CardLogged.Visibility = Visibility.Collapsed
            CardNotLogged.Visibility = Visibility.Visible
        Else
            CardLogged.Visibility = Visibility.Visible
            CardNotLogged.Visibility = Visibility.Collapsed
            TextUsername.Text = "已登录 StellarFrp"
            TextStatus.Text = "已授权使用 StellarFrp 隧道"
            RefreshStellarNodes()
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
            Config.Link.StellarTokenConfig.Reset()
            BtnLogin.Visibility = Visibility.Visible
            BtnRegister.Visibility = Visibility.Visible
            BtnCancel.Visibility = Visibility.Collapsed
            TextLogin.Text = "登录 StellarFrp 以使用中心转发隧道"
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

    Private Sub RefreshStellarNodes()
        If String.IsNullOrWhiteSpace(Config.Link.StellarToken) Then Return
        RunInNewThread(Sub()
                           Try
                                   Dim nodes = StellarFrpApi.GetNodesV2(Config.Link.StellarToken)
                               Dim suggestions = nodes.Where(Function(n) n.Status = 1 AndAlso n.AllowedTypes IsNot Nothing AndAlso n.AllowedTypes.Contains("TCP")).OrderByDescending(Function(n) n.Bandwidth).ToList()
                               RunInUi(Sub()
                                           ComboStellarNode.Items.Clear()
                                           For Each n In suggestions
                                               ComboStellarNode.Items.Add(New MyComboBoxItem() With {.Tag = n.Id, .Content = n.Name})
                                           Next
                                           Dim selectedId = Config.Link.SelectedNodeId
                                           If selectedId > 0 Then
                                               Dim idx = -1
                                               For i = 0 To ComboStellarNode.Items.Count - 1
                                                   Dim it = CType(ComboStellarNode.Items(i), MyComboBoxItem)
                                                   If CType(it.Tag, Integer) = selectedId Then idx = i : Exit For
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

End Class
