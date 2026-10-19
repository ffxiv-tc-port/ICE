using System.Collections.Generic;

using JobPairs = (string job, uint territoryId, float x, float y);
namespace ICE.Scheduler.Handlers
{
    using LocationEntry = KeyValuePair<string, (JobPairs first, JobPairs second)[]>;
    internal static unsafe class AnnouncementHandlers
    {
        private static readonly string Announcement = "WKSAnnounce";

        private static readonly Dictionary<string, (JobPairs first, JobPairs second)[]> sinusRedAlert = new()
        {
            {
                "meteorite shower",
                new(JobPairs first, JobPairs second)[]
                {
                    (
                        ("ARM/GSM", 1237, 22.6f, 14.9f),
                        ("BSM/LTW/MIN", 1237, 16.3f, 24.4f)
                    )
                }
            },
            {
                "sporing mist",
                new(JobPairs first, JobPairs second)[]
                {
                    (
                        ("CUL/BTN/FSH", 1237, 24.5f, 16.8f),
                        ("CRP/LTW/WVR", 1237, 29.0f, 35.4f)
                    ),
                    (
                        ("BSM/ALC", 1237, 32.2f, 22.2f),
                        ("CRP/WVR/BTN", 1237, 36.0f, 23.4f)
                    )
                }
            },
            {
                "astromagnetic storm",
                new(JobPairs first, JobPairs second)[]
                {
                    (
                        ("ARM/GSM/ALC", 1237, 24.9f, 33.1f),
                        ("MIN/FSH", 1237, 19.2f, 15.0f)
                    ),
                    (
                        ("CRP/GSM/WVR", 1237, 19.8f, 36.8f),
                        ("CUL/MIN/FSH", 1237, 12.3f, 20.0f)
                    )
                }
            },
        };

        internal static LocationEntry CheckForRedAlert()
        {
            if (!PlayerHelper.IsInCosmicZone()) return default;
            try
            {
                if (AddonHelper.IsAddonActive(Announcement))
                {
                    // GetAtkTextNode 取不到節點會回 null，直接解參考就是 AVE。
                    // 取不到時與「節點存在但不可見」走同一條路(回 default)。
                    var redAlertNode = AddonHelper.GetAtkTextNode(Announcement, 48); // Red Alert Preparation
                    if (redAlertNode != null && redAlertNode->IsVisible())
                    {
                        var description = AddonHelper.GetNodeText(Announcement, 47).ToLower();

                        Dictionary<string, (JobPairs first, JobPairs second)[]>? redAlert = default;
                        if (PlayerHelper.IsInSinusArdorum()) redAlert = sinusRedAlert; //Reassign based on Territory

                        if (redAlert == default) return default;
                        return redAlert.FirstOrDefault(location => description.Contains(location.Key));
                    }
                    else
                    {
                        return default;
                    }
                }
                else
                {
                    return default;
                }
            }
            catch (Exception)
            {
                return default;
            }
        }
    }
}