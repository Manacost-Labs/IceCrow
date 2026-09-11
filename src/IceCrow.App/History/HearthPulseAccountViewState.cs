using IceCrow.ProfileSync;

namespace IceCrow.App.History;

internal sealed record HearthPulseAccountViewState(
    string Title,
    string Description,
    string ActionText,
    bool IsActionEnabled,
    bool IsDisconnectAction,
    bool ShowCancel,
    bool ShowCode,
    string UserCode,
    string ExpiresText,
    Uri? VerificationUri)
{
    public static HearthPulseAccountViewState Create(ProfileSyncStatus status, ProfileLinkUpdate? link)
    {
        ArgumentNullException.ThrowIfNull(status);
        var waiting = link?.Stage is ProfileLinkStage.Starting or ProfileLinkStage.WaitingForApproval;
        var linked = IsLinked(status.Phase) || link?.Stage == ProfileLinkStage.Linked;
        var (title, description) = Describe(status, link, linked);
        return new HearthPulseAccountViewState(
            title,
            description,
            linked ? "Отключить HearthPulse" : "Подключить HearthPulse",
            !waiting,
            linked,
            waiting,
            link?.Stage == ProfileLinkStage.WaitingForApproval,
            link?.UserCode ?? "—",
            link?.ExpiresAt is { } expires ? $"Код действует до {expires.ToLocalTime():t}" : "—",
            link?.VerificationUri);
    }

    private static (string Title, string Description) Describe(
        ProfileSyncStatus status,
        ProfileLinkUpdate? link,
        bool linked) => link?.Stage switch
        {
            ProfileLinkStage.Starting => ("Создаём код", "Подключаемся к HearthPulse…"),
            ProfileLinkStage.WaitingForApproval => ("Подтвердите вход", "Откройте HearthPulse в браузере и подтвердите этот IceCrow."),
            ProfileLinkStage.Denied => ("Подключение отклонено", "Запрос можно запустить ещё раз."),
            ProfileLinkStage.Expired => ("Срок кода истёк", "Создайте новый код подключения."),
            ProfileLinkStage.Cancelled => ("Подключение отменено", "Матчи по-прежнему сохраняются локально."),
            ProfileLinkStage.Unavailable => ("HearthPulse недоступен", "Локальная история продолжает работать. Повторите позже."),
            _ when linked => ("Профиль подключён", $"Синхронизация активна · в очереди {status.PendingEvents}"),
            _ when status.Phase == ProfileSyncPhase.AuthorizationRequired => ("Нужно подключиться снова", "HearthPulse отклонил старое разрешение."),
            _ => ("Профиль не подключён", "Матчи сохраняются локально. Подключение добавит профильную синхронизацию."),
        };

    private static bool IsLinked(ProfileSyncPhase phase) =>
        phase is ProfileSyncPhase.Idle or ProfileSyncPhase.Uploading or ProfileSyncPhase.BackingOff;
}
