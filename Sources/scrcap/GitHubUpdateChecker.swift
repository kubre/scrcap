import Foundation
import ScrcapCore

struct GitHubRelease: Decodable {
    let tagName: String
    let name: String?
    let htmlURL: URL

    enum CodingKeys: String, CodingKey {
        case tagName = "tag_name"
        case name
        case htmlURL = "html_url"
    }
}

enum UpdateCheckResult {
    case updateAvailable(currentVersion: String, release: GitHubRelease)
    case upToDate(currentVersion: String, release: GitHubRelease)
    case cannotCompare(currentVersion: String, release: GitHubRelease)
}

enum UpdateCheckError: LocalizedError {
    case badResponse
    case requestFailed(Int)

    var errorDescription: String? {
        switch self {
        case .badResponse:
            return "GitHub returned an invalid response."
        case .requestFailed(let status):
            return "GitHub returned HTTP \(status)."
        }
    }
}

final class GitHubUpdateChecker {
    private let latestReleaseURL = URL(string: "https://api.github.com/repos/kubre/scrcap/releases/latest")!
    private let session: URLSession

    init(session: URLSession = .shared) {
        self.session = session
    }

    func check(currentVersion: String) async throws -> UpdateCheckResult {
        let release = try await latestRelease()
        guard ReleaseVersion(currentVersion) != nil,
              ReleaseVersion(release.tagName) != nil
        else {
            return .cannotCompare(currentVersion: currentVersion, release: release)
        }
        if ReleaseVersion.updateAvailable(current: currentVersion, latest: release.tagName) {
            return .updateAvailable(currentVersion: currentVersion, release: release)
        }
        return .upToDate(currentVersion: currentVersion, release: release)
    }

    private func latestRelease() async throws -> GitHubRelease {
        var request = URLRequest(url: latestReleaseURL)
        request.timeoutInterval = 10
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("scrcap", forHTTPHeaderField: "User-Agent")

        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw UpdateCheckError.badResponse
        }
        guard (200..<300).contains(http.statusCode) else {
            throw UpdateCheckError.requestFailed(http.statusCode)
        }
        return try JSONDecoder().decode(GitHubRelease.self, from: data)
    }
}
