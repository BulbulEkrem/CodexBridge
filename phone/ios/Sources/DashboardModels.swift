import Foundation

// dashboard/v1 snapshot şemasının Codable karşılığı.
// CodexBridge host (Windows) ve codexbar serve (macOS/Linux) aynı şemayı üretir.

struct DashboardSnapshot: Codable {
    var schemaVersion: Int = 1
    var generatedAt: String
    var staleAfterSeconds: Int = 180
    var host: HostInfo
    var providers: [ProviderRow] = []
}

struct HostInfo: Codable {
    var codexBarVersion: String?
    var refreshIntervalSeconds: Int = 0
}

struct ProviderRow: Codable {
    var id: String
    var name: String
    var enabled: Bool = true
    var source: String?
    var status: ProviderStatus?
    var identity: ProviderIdentity?
    var windows: [RateWindow] = []
    var credits: CreditInfo?
    var cost: CostInfo?
    var display: DisplayHints?
    var error: ProviderError?
    var updatedAt: String?
}

struct ProviderStatus: Codable {
    var level: String = "unknown"
    var label: String?
    var updatedAt: String?
}

struct ProviderIdentity: Codable {
    var accountEmail: String?
    var plan: String?
}

struct RateWindow: Codable {
    var kind: String
    var label: String?
    var usedPercent: Double?
    var remainingPercent: Double?
    var resetAt: String?
}

struct CreditInfo: Codable {
    var remaining: Double?
    var unit: String?
}

struct CostInfo: Codable {
    var todayUSD: Double?
    var last30DaysUSD: Double?
}

struct DisplayHints: Codable {
    var accentColor: String?
    var sortKey: Int = 0
    var priority: String?
}

struct ProviderError: Codable {
    var code: String?
    var message: String?
}
