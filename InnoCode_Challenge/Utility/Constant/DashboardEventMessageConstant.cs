using Utility.Enums;

namespace Utility.Constant
{
    public class DashboardEventMessageConstant
    {
        public static string GetEventMessage(DashboardEventTypeEnum eventType)
        {
            return eventType switch
            {
                DashboardEventTypeEnum.TeamRegistered => "New team registered",
                DashboardEventTypeEnum.AppealSubmitted => "New appeal submitted",
                DashboardEventTypeEnum.AppealResolved => "Appeal was resolved",
                DashboardEventTypeEnum.CertificateIssued => "Certificate issued",
                DashboardEventTypeEnum.ContestStatusChanged => "Contest status changed",
                DashboardEventTypeEnum.TeamEliminated => "Team was eliminated",
                DashboardEventTypeEnum.TeamDisqualified => "Team was disqualified",
                _ => "Dashboard updated"
            };
        }
    }
}
