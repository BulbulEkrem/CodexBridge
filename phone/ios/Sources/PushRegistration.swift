import Foundation

/// Faz 7: iOS cihazının APNs token'ını CodexBridge host'una kaydeder.
/// Host, kota eşiği aşıldığında (uyarı/kritik/sıfırlama) buraya push iter — böylece
/// widget'ın dar yenileme bütçesini (iOS ~40–70/gün) yoklamaya harcamak gerekmez.
///
/// Kullanım (AppDelegate):
///   func application(_:didRegisterForRemoteNotificationsWithDeviceToken tokenData: Data) {
///       Task { try? await PushRegistration(host: host).register(apnsToken: tokenData) }
///   }
struct PushRegistration {
    let host: DashboardClient.Host

    /// APNs token Data'sını hex string'e çevirip host'a POST eder.
    func register(apnsToken tokenData: Data, label: String? = nil) async throws {
        let hex = tokenData.map { String(format: "%02x", $0) }.joined()
        try await send(method: "POST", token: hex, label: label)
    }

    /// Kaydı siler (kullanıcı bildirimi kapattığında / oturumu kapattığında).
    func unregister(apnsToken tokenData: Data) async throws {
        let hex = tokenData.map { String(format: "%02x", $0) }.joined()
        try await send(method: "DELETE", token: hex, label: nil)
    }

    private func send(method: String, token: String, label: String?) async throws {
        guard let url = URL(string: "\(host.baseUrl.trimmingTrailingSlashPush())/dashboard/v1/devices") else {
            throw URLError(.badURL)
        }
        var req = URLRequest(url: url)
        req.httpMethod = method
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if let t = host.token { req.setValue("Bearer \(t)", forHTTPHeaderField: "Authorization") }

        var body: [String: String] = ["token": token, "platform": "apns"]
        if let label { body["label"] = label }
        req.httpBody = try JSONSerialization.data(withJSONObject: body)

        let (_, resp) = try await URLSession.shared.data(for: req)
        guard let http = resp as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw URLError(.userAuthenticationRequired)
        }
    }
}

private extension String {
    func trimmingTrailingSlashPush() -> String { hasSuffix("/") ? String(dropLast()) : self }
}
