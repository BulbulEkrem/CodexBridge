import WidgetKit
import Foundation

/// WidgetKit girişi: son bilinen satırlar + verinin çekildiği an (yaş göstermek için).
struct UsageEntry: TimelineEntry {
    let date: Date
    let rows: [ProviderRow]
    let fetchedAt: Date
}

/// Timeline'ı SABİT aralıkla değil, kotaların `resetAt`'ine HİZALAYARAK üretir.
/// iOS bütçesi (~40–70/gün) dar; yenilemeyi sıfırlanma anlarına harcamak en verimlisi.
struct UsageTimelineProvider: TimelineProvider {
    let hosts: [DashboardClient.Host]

    func placeholder(in context: Context) -> UsageEntry {
        UsageEntry(date: .now, rows: [], fetchedAt: .distantPast)
    }

    func getSnapshot(in context: Context, completion: @escaping (UsageEntry) -> Void) {
        Task {
            let rows = await DashboardClient().fetchMerged(hosts)
            completion(UsageEntry(date: .now, rows: rows, fetchedAt: .now))
        }
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<UsageEntry>) -> Void) {
        Task {
            let rows = await DashboardClient().fetchMerged(hosts)
            let now = Date.now
            let entry = UsageEntry(date: now, rows: rows, fetchedAt: now)

            // Yenileme noktaları: her kotanın resetAt'i (+1 dk) + güvenlik için 1 saatlik taban.
            let iso = ISO8601DateFormatter()
            var refreshDates: [Date] = rows
                .flatMap { $0.windows }
                .compactMap { $0.resetAt.flatMap(iso.date(from:)) }
                .map { $0.addingTimeInterval(60) }
                .filter { $0 > now }
            refreshDates.append(now.addingTimeInterval(3600))
            let next = refreshDates.min() ?? now.addingTimeInterval(3600)

            completion(Timeline(entries: [entry], policy: .after(next)))
        }
    }
}
