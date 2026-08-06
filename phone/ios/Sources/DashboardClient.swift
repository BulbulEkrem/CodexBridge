import Foundation

/// Bir veya birden fazla dashboard/v1 host'undan snapshot çeker.
/// Çok-makineli birleşik görünüm: maliyet toplanır, kota yüzdeleri toplanmaz (hesap bazlı; id tekil).
struct DashboardClient {
    struct Host { let baseUrl: String; let token: String? }

    func fetch(_ host: Host) async throws -> DashboardSnapshot {
        guard let url = URL(string: "\(host.baseUrl.trimmingTrailingSlash())/dashboard/v1/snapshot") else {
            throw URLError(.badURL)
        }
        var req = URLRequest(url: url)
        if let token = host.token { req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization") }
        let (data, resp) = try await URLSession.shared.data(for: req)
        guard let http = resp as? HTTPURLResponse, http.statusCode == 200 else {
            throw URLError(.userAuthenticationRequired)
        }
        return try JSONDecoder().decode(DashboardSnapshot.self, from: data)
    }

    func fetchMerged(_ hosts: [Host]) async -> [ProviderRow] {
        var byId: [String: ProviderRow] = [:]
        var order: [String] = []
        for host in hosts {
            guard let snap = try? await fetch(host) else { continue }
            for row in snap.providers {
                if var existing = byId[row.id] {
                    // Maliyet topla, kota pencerelerini tekil tut.
                    var cost = existing.cost ?? CostInfo()
                    cost.todayUSD = (cost.todayUSD ?? 0) + (row.cost?.todayUSD ?? 0)
                    cost.last30DaysUSD = (cost.last30DaysUSD ?? 0) + (row.cost?.last30DaysUSD ?? 0)
                    existing.cost = cost
                    byId[row.id] = existing
                } else {
                    byId[row.id] = row
                    order.append(row.id)
                }
            }
        }
        return order.compactMap { byId[$0] }.sorted { ($0.display?.sortKey ?? 0) < ($1.display?.sortKey ?? 0) }
    }
}

private extension String {
    func trimmingTrailingSlash() -> String { hasSuffix("/") ? String(dropLast()) : self }
}
