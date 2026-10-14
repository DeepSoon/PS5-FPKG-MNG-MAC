import AppKit
import Foundation
import UniformTypeIdentifiers
import Observation
import UniformTypeIdentifiers

// MARK: - Backend discovery + invocation (Made by Leite)

func backendURL() -> URL? {
    let res = Bundle.main.resourceURL
    let cands: [URL?] = [
        res?.appendingPathComponent("FpkgMac/FpkgMac"),
        res?.appendingPathComponent("FpkgMac/FpkgMac.dll"),
    ]
    for c in cands {
        if let u = c, FileManager.default.isExecutableFile(atPath: u.path) || FileManager.default.fileExists(atPath: u.path) {
            return u
        }
    }
    return nil
}

func backendCommand(_ args: [String]) -> [String]? {
    guard let be = backendURL() else { return nil }
    if be.pathExtension == "dll" { return ["/usr/bin/dotnet", be.path] + args }
    return [be.path] + args
}

/// Quick synchronous call (fast commands only). Returns (exitCode, stdout, stderr).
func runBackendSync(_ args: [String], timeout: TimeInterval = 120) -> (Int32, String, String) {
    guard let cmd = backendCommand(args) else { return (127, "", "backend missing") }
    let p = Process()
    p.executableURL = URL(fileURLWithPath: cmd[0])
    p.arguments = Array(cmd.dropFirst())
    let out = Pipe(), err = Pipe()
    p.standardOutput = out
    p.standardError = err
    do { try p.run() } catch { return (127, "", error.localizedDescription) }
    let deadline = Date().addingTimeInterval(timeout)
    while p.isRunning {
        if Date() > deadline { p.terminate(); return (124, "", "timeout") }
        Thread.sleep(forTimeInterval: 0.05)
    }
    let o = String(data: out.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
    let e = String(data: err.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
    return (p.terminationStatus, o, e)
}

// MARK: - Shared app state

enum Page: String, CaseIterable {
    case reader, files, build, ampr, extractor, trophy, about
}

// Observable without property-wrapper macros (CLT toolchain lacks SwiftUIMacros).
@Observable
final class AppState {
    var lang: Lang {
        didSet { UserDefaults.standard.set(lang.rawValue, forKey: "lang") }
    }
    var dark: Bool {
        didSet { UserDefaults.standard.set(dark, forKey: "dark") }
    }
    var page: Page = .reader
    var backendStatus: String
    var backendOK: Bool = false
    // live transfer progress (driven by streaming tasks)
    var progStage = ""
    var progDetail = ""
    var progDone: Int64 = 0
    var progTotal: Int64 = 0
    var progSpeed: Double = 0
    var progActive = false
    var statusText: String
    // page models (stable identity; views observe them directly)
    let reader = ReaderModel()
    let files = FilesModel()
    let build = BuildModel()
    let ampr = AmprModel()
    let extractor = ExtractorModel()
    let trophy = TrophyModel()

    init() {
        // migrate legacy Python-GUI settings once
        var l = Lang.ptBR
        var d = false
        let legacy = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/PS5 FPKG Builder/settings.json")
        if let data = try? Data(contentsOf: legacy),
           let j = try? JSONSerialization.jsonObject(with: data) as? [String: String] {
            if j["lang"] == "en" { l = .en }
            if j["theme"] == "dark" { d = true }
        }
        if let s = UserDefaults.standard.string(forKey: "lang") { l = (s == "en" ? .en : .ptBR) }
        d = UserDefaults.standard.object(forKey: "dark") as? Bool ?? d
        lang = l
        dark = d
        backendStatus = tr("backend_checking", l)
        statusText = tr("ready", l)
    }

    func t(_ key: String, _ args: [String: String] = [:]) -> String {
        tr(key, lang, args)
    }
}

// MARK: - Page models (observed directly by views; no wrappers needed)

@Observable
final class ReaderModel {
    var source = ""
    var passcode = String(repeating: "0", count: 32)
    var rows: [KVRow] = []
    var raw = ""
    var working = false
    var alertMsg = ""
    var showAlert = false
}

@Observable
final class FilesModel {
    var pkg = ""
    var passcode = String(repeating: "0", count: 32)
    var filter = ""
    var all: [FileRow] = []
    var sel: FileRow.ID?
    var working = false
    var alertMsg = ""
    var showAlert = false
}

@Observable
final class BuildModel {
    var isImage = false
    var source = ""
    var output = ""
    var tmp = (NSHomeDirectory() as NSString).appendingPathComponent("Downloads/fpkg-tmp")
    var passcode = String(repeating: "0", count: 32)
    var sdk = "9.00"
    var mode = "Application"
    var log = ""
    var alertMsg = ""
    var showAlert = false
    var showDone = false
    var donePath = ""
}

@Observable
final class ExtractorModel {
    var pkg = ""
    var dest = ""
    var tmp = (NSHomeDirectory() as NSString).appendingPathComponent("Downloads/fpkg-tmp")
    var passcode = String(repeating: "0", count: 32)
    var log = ""
    var alertMsg = ""
    var showAlert = false
}

@Observable
final class TrophyModel {    var host = ""
    var ppsa = ""
    var rows: [TrophyRow] = []
    var sel: TrophyRow.ID?
    var info = ""
    var working = false
    var alertMsg = ""
    var showAlert = false
    var confirmAll = false
}

@Observable
final class AmprModel {
    var isImage = false
    var source = ""
    var output = ""
    var packDir = ""
    var dest = ""
    var level: Double = 9
    var log = ""
    var alertMsg = ""
    var showAlert = false
    var showDone = false
    var donePath = ""
}

// MARK: - Streaming task engine (event-driven, zero polling)
//
// Reads the backend pipe via readabilityHandler (fires only when data
// arrives — no timer, no busy loop). UI repaints are throttled to ~4 Hz.

final class TaskRunner: NSObject, @unchecked Sendable {
    private var proc: Process?
    private let lockQueue = DispatchQueue(label: "fpkg.taskrunner")
    private var samples: [(Date, Int64)] = []
    private var lastPaint = Date.distantPast
    private var lastProgFire = Date.distantPast
    private var finished = false
    private let appendLog: ((String) -> Void)?
    private let onProgress: ((String, String, Int64, Int64) -> Void)?
    private let onDone: ((Int32, String) -> Void)?
    private let paint: () -> Void
    private var transcript: [String] = []

    init(appendLog: ((String) -> Void)? = nil,
         onProgress: ((String, String, Int64, Int64) -> Void)? = nil,
         onDone: ((Int32, String) -> Void)? = nil,
         paint: @escaping () -> Void) {
        self.appendLog = appendLog
        self.onProgress = onProgress
        self.onDone = onDone
        self.paint = paint
    }

    @discardableResult
    func start(_ args: [String]) -> Bool {
        guard let cmd = backendCommand(args) else { return false }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: cmd[0])
        p.arguments = Array(cmd.dropFirst())
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = pipe
        let h = pipe.fileHandleForReading
        // Event-driven: the handler runs only when bytes arrive.
        h.readabilityHandler = { [weak self] handle in
            let data = handle.availableData
            guard let self, !data.isEmpty else { return }
            if let text = String(data: data, encoding: .utf8) {
                self.ingest(text)
            }
        }
        p.terminationHandler = { [weak self] proc in
            guard let self else { return }
            // drain remainder, then finish on main thread
            var rest = ""
            do {
                if let d = try h.readToEnd(), let s = String(data: d, encoding: .utf8) { rest = s }
            } catch {}
            h.readabilityHandler = nil
            DispatchQueue.main.async {
                if !rest.isEmpty { self.ingest(rest) }
                self.finish(status: proc.terminationStatus)
            }
        }
        do {
            try p.run()
        } catch {
            return false
        }
        lockQueue.sync { self.proc = p }
        return true
    }

    func cancel() {
        lockQueue.sync { proc?.terminate(); proc = nil }
    }

    private func ingest(_ text: String) {
        var logs: [String] = []
        var prog: [(String, String, Int64, Int64)] = []
        for raw in text.components(separatedBy: "\n") {
            let line = raw.hasSuffix("\r") ? String(raw.dropLast()) : raw
            if line.hasPrefix("PROGRESS\t") {
                let parts = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
                let stage = parts.count > 1 ? parts[1] : ""
                let done = parts.count > 2 ? Int64(Double(parts[2]) ?? 0) : 0
                let total = parts.count > 3 ? Int64(Double(parts[3]) ?? 0) : 0
                let detail = parts.count > 4 ? parts[4] : ""
                prog.append((stage, detail, done, total))
            } else if !line.isEmpty {
                logs.append(line)
            }
        }
        let cbAppend = appendLog, cbProg = onProgress
        // Throttle UI repaints to ~4 Hz; always keep the freshest numbers.
        let now = Date()
        let shouldPaint: Bool = lockQueue.sync {
            if !prog.isEmpty {
                if let last = prog.last {
                    samples.append((now, last.2))
                    if samples.count > 40 { samples.removeFirst(samples.count - 40) }
                }
            }
            let due = now.timeIntervalSince(lastPaint) >= 0.25 || !prog.isEmpty && prog.last.map { $0.3 > 0 && $0.2 >= $0.3 } == true
            if due { lastPaint = now }
            return due
        }
        DispatchQueue.main.async {
            if !logs.isEmpty {
                self.lockQueue.sync { self.transcript.append(contentsOf: logs) }
                for l in logs { cbAppend?(l) }
            }
            // Throttle progress callbacks (~4 Hz); always fire completion.
            // Samples are recorded for every event so speed stays accurate.
            let fire: [(String, String, Int64, Int64)] = self.lockQueue.sync {
                guard !prog.isEmpty else { return [] }
                let isComplete = prog.last.map { $0.3 > 0 && $0.2 >= $0.3 } == true
                if Date().timeIntervalSince(self.lastProgFire) >= 0.25 || isComplete {
                    self.lastProgFire = Date()
                    return prog
                }
                return []
            }
            for (s, d, dn, t) in fire { cbProg?(s, d, dn, t) }
            if shouldPaint { self.paint() }
        }
    }

    private func finish(status: Int32) {
        let doneFinished: Bool = lockQueue.sync {
            if finished { return true }
            finished = true
            return false
        }
        if doneFinished { return }
        let text: String = lockQueue.sync { transcript.joined(separator: "\n") }
        let cb = onDone
        paint()
        cb?(status, text)
    }

    /// Speed (bytes/s) over a ~4 s sliding window; 0 when stalled > 2 s.
    func currentSpeed(done: Int64) -> Double {
        lockQueue.sync {
            let now = Date()
            samples = samples.filter { now.timeIntervalSince($0.0) <= 4.0 }
            if let last = samples.last, now.timeIntervalSince(last.0) > 2.0 { return 0 }
            guard samples.count >= 2 else { return 0 }
            let (t0, d0) = samples[0]
            let dt = now.timeIntervalSince(t0)
            guard dt >= 0.15, done > d0 else { return 0 }
            return Double(done - d0) / dt
        }
    }
}

// MARK: - Pickers + clipboard

func pickFile(title: String, extensions: [String]) -> String? {
    let p = NSOpenPanel()
    p.title = title
    p.allowsMultipleSelection = false
    p.canChooseFiles = true
    p.canChooseDirectories = false
    p.allowedContentTypes = extensions.compactMap { UTType(filenameExtension: $0) }
    return p.runModal() == .OK ? p.url?.path : nil
}

func pickFolder(title: String) -> String? {
    let p = NSOpenPanel()
    p.title = title
    p.allowsMultipleSelection = false
    p.canChooseFiles = false
    p.canChooseDirectories = true
    p.canCreateDirectories = true
    return p.runModal() == .OK ? p.url?.path : nil
}

func pickSavePKG(title: String) -> String? {
    let p = NSSavePanel()
    p.title = title
    if let t = UTType(filenameExtension: "pkg") { p.allowedContentTypes = [t] }
    p.nameFieldStringValue = "game.pkg"
    guard p.runModal() == .OK, var path = p.url?.path else { return nil }
    if (path as NSString).pathExtension.lowercased() != "pkg" { path += ".pkg" }
    return path
}

func copyToClipboard(_ s: String) {
    NSPasteboard.general.clearContents()
    NSPasteboard.general.setString(s, forType: .string)
}
