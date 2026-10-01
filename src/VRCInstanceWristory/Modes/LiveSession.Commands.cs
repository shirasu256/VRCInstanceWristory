using System.Globalization;
using VRCInstanceWristory.Core;
using VRCInstanceWristory.Core.Locations;
using VRCInstanceWristory.Core.Presentation;
using VRCInstanceWristory.Desktop;
using VRCInstanceWristory.Infrastructure;

namespace VRCInstanceWristory.Modes;

/// <summary>ウィンドウ・ダッシュボード・外部からの操作を処理する（主ループから呼ぶ）。</summary>
public sealed partial class LiveSession
{
    private void HandleCommand(DesktopCommand command, SettingsEditor source)
    {
        switch (command)
        {
            case DesktopCommand.ExtendRetention:
                _engine.ResetRetention();
                break;

            case DesktopCommand.FinishWelcome:
                _settings.WelcomeCompleted = true;
                TrySave(SettingsField.Welcome, "初回起動の案内を終えたことを設定ファイルへ保存できません（次の起動でもう一度出ます）", out _);
                break;

            case DesktopCommand.ClearHistory:
                _engine.ClearHistory();
                break;

            case DesktopCommand.SetMark m:
                _engine.SetMark(m.EventId, m.Mark);
                break;

            case DesktopCommand.ChangeSettings c:
                ChangeSettings(c.Settings, c.Fields, source);
                break;

            case DesktopCommand.ResetPlacement:
                ResetPlacement();
                break;

            case DesktopCommand.UndoClearHistory:
                _engine.UndoClearHistory();
                break;

            case DesktopCommand.OpenInstance o:
                OpenInstance(o.EventId);
                break;

            case DesktopCommand.OpenPhoto p:
                // 写真を開くアプリなどの起動には時間がかかることがあるので、主ループ（VRの描画）を止めない。
                ShellLauncher.OpenPhoto(p.Path, _settings.Viewer, _settings.PhotoViewerPath, _log);
                break;

            case DesktopCommand.OpenGroupPage g when VrChatUrls.GroupPage(g.GroupId) is { } groupPage:
                _log.Notice($"グループ {g.GroupId} のページをブラウザで開きます: {groupPage}");
                ShellLauncher.OpenUrl(groupPage, "ブラウザで開けません", _log);
                break;

            case DesktopCommand.OpenUserPage u when VrChatUrls.UserPage(u.UserId) is { } userPage:
                _log.Notice($"ユーザー {u.UserId} のページをブラウザで開きます: {userPage}");
                ShellLauncher.OpenUrl(userPage, "ブラウザで開けません", _log);
                break;

            case DesktopCommand.OpenDeveloperPage:
                ShellLauncher.OpenUrl(AppInfo.DeveloperPage, "ブラウザで開けません", _log);
                break;

            case DesktopCommand.OpenPhotoFolder f:
                ShellLauncher.ShowInFolder(f.Path, _log);
                break;

            case DesktopCommand.SetGroupName g:
                SetGroupName(g.GroupId, g.Name);
                break;

            case DesktopCommand.ImportGroupNames i:
                ImportGroupNames(i.Names);
                break;

            case DesktopCommand.ExportGroupNames e:
            {
                var (text, ok) = GroupNameEditor.Export(_settings.GroupNames, e.Path, _log);
                _desktop?.ShowNotice(text, error: !ok);
                break;
            }

            case DesktopCommand.CopyText t:
                // クリップボードが空くのを待つことがあるので、このループの外で写す（→ClipboardText）。
                ClipboardText.SetInBackground(t.Text, _log);
                break;

            case DesktopCommand.AnotherLaunch:
                // 2つ目の起動があった（→実装メモ5.52）。SteamVRが起動したところかもしれないので、すぐに確かめる。
                _watcher.Poke();
                break;

            case DesktopCommand.CheckForUpdates:
                _updater.CheckNow();
                break;

            case DesktopCommand.ApplyUpdate:
                // 落とし終えたら RunFrame が終わる（→実装メモ5.121）。
                _updater.RequestApply();
                break;
        }
    }

    private void ChangeSettings(DesktopSettings values, SettingsField fields, SettingsEditor source)
    {
        // SteamVRと一緒に起動する・ログオン時に起動するは、登録を変えて結果を返す（設定ファイルには書かない）。
        if ((fields & DesktopSettings.StartupFields) != 0)
        {
            if (fields.HasFlag(SettingsField.LaunchAtLogon))
                _settings.LaunchAtLogon = _startup.SetEnabled(values.LaunchAtLogon, _log);

            if (fields.HasFlag(SettingsField.LaunchWithSteamVr) && values.LaunchWithSteamVr is { } withSteamVr)
                _settings.LaunchWithSteamVr = _runtime?.SetAutoLaunch(withSteamVr);

            _log.Notice($"起動の登録: SteamVRと一緒に {Describe(_settings.LaunchWithSteamVr)} / Windowsのログオン時 {Describe(_settings.LaunchAtLogon)}");
            PublishStartupState();
        }

        fields &= ~DesktopSettings.StartupFields;

        if (fields == SettingsField.None)
            return;

        var wasDetecting = _settings.AfkDetectionEnabled;
        var afkBefore = (_settings.PauseCountdownWhileAfk, _settings.ResetWarningReshowAfterAfk);

        _changes.Apply(values, fields, _runtime);

        // 外部からのコマンドの受け付けは、切り替えたその場で始める・やめる（保存は落ち着いてから）。
        if (fields.HasFlag(SettingsField.ExternalReset))
            _external.SetEnabled(_settings.ExternalResetEnabled);

        // AFK を使う設定を切り替えたら、OSC の受け口を開く・閉じる（→実装メモ5.89）。
        // 「VRChatのAFKを検知する」（初期設定の画面）か AFK を使う2つの設定をオンにしたその場で開くので、
        // ファイアウォールの許可の画面もこのとき出る（→実装メモ5.98・5.99）。
        // 開いたあとにファイアウォールでブロックされていると分かったら、オンにする前の値へ戻す（→実装メモ5.111）。
        if (fields.HasFlag(SettingsField.AfkDetection))
        {
            if (!wasDetecting && _settings.AfkDetectionEnabled)
                _afkBeforeOn = afkBefore;

            SyncOscListener();
        }

        // 予告通知の設定（表示位置・大きさ・マイクアイコン位置など）を変えている間は、VR 内にアイコンを点けたままにする（→実装メモ5.92）。
        if (fields.HasFlag(SettingsField.ResetWarning) && _settings.ResetWarningEnabled)
            _runtime?.PreviewResetWarning();

        // グループ名の出し方が変われば、行を作り直す（→実装メモ5.48）。
        if (fields.HasFlag(SettingsField.GroupDisplay))
            RefreshGroupNames();

        // 設定の画面は2つある。変えた側は自分の写しを既に持っているので、もう一方へだけ知らせる。
        BroadcastSettings(except: source);
    }

    /// <summary>
    /// AFK の受け口がファイアウォールでブロックされていた（2026-10-01のユーザー指定→実装メモ5.111）。
    /// トグルがオンのまま何も検知しない状態にしないよう、検知をオフに戻して受け口を閉じる。
    /// 利用者がオンにしたときは、AFK を使う2つの値もオンにする前へ戻し、直し方を知らせる。
    /// 起動時に開いたときは知らせない（VR の最中に勝手に画面を出さない）。
    /// </summary>
    private void RevertBlockedAfk()
    {
        var before = _afkBeforeOn;
        var current = DesktopSettings.From(_settings);
        var revert = before is { } b
            ? current with { AfkDetectionEnabled = false, PauseCountdownWhileAfk = b.Pause, WarningReshowAfterAfk = b.Reshow }
            : current with { AfkDetectionEnabled = false };

        _changes.Apply(revert, SettingsField.AfkDetection | SettingsField.AfkPause | SettingsField.ResetWarning, _runtime);
        SyncOscListener();
        _log.Warn($"Windows のファイアウォールでこのアプリの受信がブロックされているため、VRChatのAFKの検知をオフに戻しました: {Environment.ProcessPath}");

        // 押した側の画面もオンの写しを持っているので、両方へ送る。
        BroadcastSettings();

        if (before is not null)
            ShowBlockedNotice(AfkFirewallWatch.BlockedNotice(Environment.ProcessPath));
    }

    /// <summary>ウィンドウがあればその上に、なければ単独の小さな画面で知らせる（主ループは待たせない）。</summary>
    private void ShowBlockedNotice(string text)
    {
        if (_desktop is not null)
        {
            _desktop.ShowNotice(text, error: true);
            return;
        }

        var thread = new Thread(() => NativeMethods.MessageBoxW(0, text, AppInfo.DisplayName, NativeMethods.MB_OK | NativeMethods.MB_ICONERROR))
        {
            IsBackground = true,
            Name = "Firewall notice",
        };
        thread.Start();
    }

    private static string Describe(bool? enabled) => enabled switch
    {
        true => "オン",
        false => "オフ",
        null => "（SteamVRにつながっていないため不明）",
    };

    /// <summary>外部からのコマンド（→実装メモ5.83）。確認を挟まずに、手首のパネルの「リセット」と同じく訪問履歴を今すぐ消す。</summary>
    private void TakeExternalCommands()
    {
        while (_external.TryTake(out var command))
        {
            if (command != ExternalCommand.ResetHistory || !_settings.ExternalResetEnabled)
                continue;

            _engine.ClearHistory();

            var resetAt = SystemClock.Instance.UtcNow;
            _settings.ExternalResetLastRunUtc = resetAt;
            ExternalResetRecord.Save(AppPaths.ExternalReset, resetAt, _log);
            _log.Notice("外部からのコマンドで訪問履歴をリセットしました。");

            // 「最終実行」は両方の設定の画面に出す。
            BroadcastSettings();
        }
    }

    /// <summary>
    /// 行のインスタンスを開く（2026-09-26のユーザー指定→実装メモ5.43・5.53）。VRChat API は使わない。
    ///
    /// <list type="bullet">
    /// <item>ブラウザ（既定）: その訪問のログに書かれていた location から vrchat.com のインスタンスのページを作り、既定のブラウザで開く。
    /// 動いている VRChat には何も起きないので、いま滞在しているインスタンスでも開く</item>
    /// <item>VRChat: <c>vrchat://launch</c> の URL を Windows に開いてもらう（VRChat が URL スキームを受け持つ）。
    /// VRChat はクライアントを起動し直すので、いま滞在しているインスタンスでは開かない。
    /// 開いたら、状態の段に「再起動中」と出す（→実装メモ5.85）</item>
    /// </list>
    /// </summary>
    private void OpenInstance(string eventId)
    {
        if (_engine.FindVisit(eventId) is not { } visit)
            return;

        if (_settings.OpenAction == ReturnAction.Browser)
        {
            if (InstanceLaunch.WebUrlFor(visit.Location) is not { } page)
            {
                _log.Warn($"インスタンス {visit.InstanceId} の場所を URL にできません。");
                return;
            }

            _log.Notice($"インスタンス {visit.InstanceId} のページをブラウザで開きます: {page}");
            ShellLauncher.OpenUrl(page, "ブラウザで開けません", _log);
            return;
        }

        var snapshot = _engine.Snapshot();
        var here = RowFormatter.CurrentLocationKey(snapshot.History, snapshot.CurrentEventId);

        if (here is not null && string.Equals(here, visit.LocationKey, StringComparison.Ordinal))
        {
            _log.Info($"インスタンス {visit.InstanceId} にはいま滞在しているので開きません。");
            return;
        }

        if (InstanceLaunch.UrlFor(visit.Location) is not { } url)
        {
            _log.Warn($"インスタンス {visit.InstanceId} の場所を VRChat へ渡せる形にできません。");
            return;
        }

        _log.Notice($"インスタンス {visit.InstanceId} へ戻ります（VRChatで開きます。VRChatは起動し直します）: {url}");
        ShellLauncher.OpenUrl(url, "VRChatで開けません（vrchat:// が登録されていない可能性があります）", _log);
        _relaunchAt = SystemClock.Instance.UtcNow;
    }

    /// <summary>グループに名前を付ける・外す（→実装メモ5.48）。付けたらすぐに設定ファイルへ書く。</summary>
    private void SetGroupName(string groupId, string? name)
    {
        if (!GroupNameEditor.Set(_settings, groupId, name, _log))
            return;

        if (TrySave(SettingsField.GroupNames, "グループ名を保存できません", out _))
        {
            _log.Notice(_settings.GroupNames.GetValueOrDefault(groupId) is { } named
                ? $"グループ {groupId} に名前「{named}」を付けました。"
                : $"グループ {groupId} の名前を外しました。");
        }

        RefreshGroupNames();
    }

    /// <summary>ファイルから読んだグループ名を取り込む（→実装メモ5.65）。取り込んだらすぐに設定ファイルへ書き、結果をウィンドウに出す。</summary>
    private void ImportGroupNames(IReadOnlyDictionary<string, string> names)
    {
        var result = GroupNameEditor.Import(_settings, names);
        var text = result.Summary;
        var ok = true;

        if (result.Changed)
        {
            if (TrySave(SettingsField.GroupNames, "グループ名を保存できません", out var error))
            {
                _log.Notice(text);
            }
            else
            {
                text += $"\nただし設定ファイルへ保存できませんでした: {error}";
                ok = false;
            }

            RefreshGroupNames();
        }

        _desktop?.ShowNotice(text, error: !ok);
    }

    private void RefreshGroupNames()
    {
        _presenter.Groups = _settings.GroupNaming;
        _presenter.Refresh();
    }

    /// <summary>
    /// 「手首パネルの位置をデフォルトに戻す」（2026-09-28のユーザー指定→5.39・5.65・5.86）。名前のとおり配置（位置と角度）だけを戻す。
    /// 5.69 では手首パネルのまとまりの値もすべて戻していたが、それはやめた。付ける手首も変えない。
    /// いまパネルを付けている手首の配置だけを戻す（→5.49）。SteamVRにつながっていなくても設定は戻す。
    /// </summary>
    private void ResetPlacement()
    {
        var side = _runtime?.Controller.Wrist ?? _settings.Wrist;

        _settings.ResetPlacement(side);

        var (translation, rotation) = _settings.PlacementFor(side);
        _runtime?.Controller.SetPlacement(translation, rotation);
        SavePlacement(side);
    }

    /// <summary>その手首の配置を設定ファイルへ書き戻す。</summary>
    private void SavePlacement(WristSide side)
    {
        if (!TrySave(AppSettings.PlacementField(side), "パネルの配置を保存できません", out _))
            return;

        var (translation, rotation) = _settings.PlacementFor(side);
        var (pitch, yaw, roll) = Rotations.ToEulerDegrees(rotation);
        _log.Notice(string.Create(
            CultureInfo.InvariantCulture,
            $"パネルの配置（{WristSides.DisplayName(side)}）を保存しました: 位置 [{translation.X:0.000}, {translation.Y:0.000}, {translation.Z:0.000}] / 角度 [{pitch:0.0}, {yaw:0.0}, {roll:0.0}]"));
    }

    /// <summary>
    /// 設定ファイルへすぐに書き戻す。書けたかを状態の段の「履歴保存不能」に写す（→実装メモ5.85）。
    /// 書けなければ「<paramref name="failure"/>: 理由」を PC 側へ出す。
    /// </summary>
    private bool TrySave(SettingsField fields, string failure, out string? error)
    {
        var saved = _settings.TrySaveFields(_settingsPath, fields, _log, failure, out error);
        _settingsSaveFailing = !saved;
        return saved;
    }
}
