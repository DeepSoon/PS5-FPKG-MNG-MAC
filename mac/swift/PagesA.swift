import SwiftUI

// MARK: - Task helper shared by pages

/// Starts a streaming backend task wired to global progress + a log setter.
@discardableResult
func startTask(_ state: AppState, _ args: [String],
               setLog: ((String) -> Void)? = nil,
               appendLog: ((String) -> Void)? = nil,
               status: String,
               onDone: ((Int32, String) -> Void)? = nil) -> TaskRunner? {
    if state.progActive { return nil }
    state.progActive = true
    state.progDone = 0
    state.progTotal = 0
    state.progSpeed = 0
    state.progStage = status
    state.progDetail = ""
    state.statusText = status
    var runner: TaskRunner!
    runner = TaskRunner(
        appendLog: appendLog,
        onProgress: { stage, detail, done, total in
            state.progStage = stage
            state.progDetail = detail
            if total > 0 {
                state.progDone = done
                state.progTotal = total
                state.progSpeed = runner.currentSpeed(done: done)
            }
            state.statusText = detail.isEmpty ? stage : "\(stage) — \(detail)"
        },
        onDone: { rc, transcript in
            state.progActive = false
            if rc == 0, state.progTotal > 0 {
                state.progDone = state.progTotal
                state.progSpeed = runner.currentSpeed(done: state.progDone)
            }
            onDone?(rc, transcript)
        },
        paint: {}
    )
    if !runner.start(args) {
        state.progActive = false
        return nil
    }
    if let set = setLog { set("") }
    return runner
}

func bind<T>(_ get: @escaping () -> T, _ set: @escaping (T) -> Void) -> Binding<T> {
    Binding(get: get, set: set)
}

// MARK: - Reader

struct KVRow: Identifiable {
    let id = UUID()
    let key: String
    let value: String
    let raw: Bool
}

struct ReaderView: View {
    let state: AppState
    private var m: ReaderModel { state.reader }

    var body: some View {
        PageHeader(kicker: state.t("reader_kicker"), title: state.t("reader_title"), desc: state.t("reader_desc"), dark: state.dark)
        Card(dark: state.dark) {
            VStack(alignment: .leading, spacing: 8) {
                FieldLabel(text: state.t("source_label"), dark: state.dark)
                HStack {
                    TextField(state.t("source_ph"), text: bind({ m.source }, { m.source = $0 }))
                        .dsField(state.dark)
                    Button(state.t("btn_folder")) {
                        if let d = pickFolder(title: state.t("btn_folder")) { m.source = d }
                    }.buttonStyle(DSGhost(dark: state.dark))
                    Button(state.t("btn_pkg")) {
                        if let p = pickFile(title: state.t("btn_pkg"), extensions: ["pkg"]) { m.source = p }
                    }.buttonStyle(DSGhost(dark: state.dark))
                }
                HStack {
                    Text(state.t("passcode_label")).font(.system(size: 13, weight: .semibold))
                    SecureField("", text: bind({ m.passcode }, { m.passcode = $0 }))
                        .dsField(state.dark).frame(width: 300)
                    Button(state.t("btn_inspect")) { inspect() }.buttonStyle(DSPillPrimary(dark: state.dark))
                        .buttonStyle(DSPillPrimary(dark: state.dark)).disabled(m.working || state.progActive)
                    Button(state.t("btn_clear")) { m.rows = []; m.raw = "" }.buttonStyle(DSGhost(dark: state.dark))
                    Button(state.t("btn_copy")) { copyToClipboard(m.raw) }.buttonStyle(DSGhost(dark: state.dark))
                }.padding(.top, 4)
            }
        }
        Card(dark: state.dark) {
            VStack(alignment: .leading, spacing: 8) {
                Text(state.t("result_label")).font(.system(size: 13, weight: .semibold))
                if m.rows.isEmpty {
                    Text(state.t("result_empty")).foregroundColor(.secondary).font(.system(size: 12))
                } else {
                    VStack(alignment: .leading, spacing: 3) {
                        ForEach(m.rows) { r in
                            if r.raw {
                                Text(r.value).font(.system(size: 11, design: .monospaced))
                                    .foregroundColor(.secondary)
                            } else {
                                HStack(alignment: .firstTextBaseline, spacing: 10) {
                                    Text(r.key).font(.system(size: 11)).foregroundColor(.secondary)
                                        .frame(width: 170, alignment: .trailing)
                                    Text(r.value.isEmpty ? "—" : r.value).font(.system(size: 12))
                                        .textSelection(.enabled)
                                }
                            }
                        }
                    }
                }
            }
        }
        .alert(state.t("err_title"), isPresented: bind({ m.showAlert }, { m.showAlert = $0 })) {
            Button("OK", role: .cancel) {}
        } message: { Text(m.alertMsg) }
    }

    private func inspect() {
        let src = m.source.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !src.isEmpty else {
            m.alertMsg = state.t("ask_src"); m.showAlert = true; return
        }
        m.working = true
        state.statusText = state.t("working")
        var isDirFlag = ObjCBool(false)
        let isDir = FileManager.default.fileExists(atPath: src, isDirectory: &isDirFlag) && isDirFlag.boolValue
        let args: [String] = isDir
            ? ["inspect-folder", src]
            : ["inspect-pkg", src, m.passcode.isEmpty ? String(repeating: "0", count: 32) : m.passcode]
        DispatchQueue.global(qos: .userInitiated).async {
            let (rc, out, err) = runBackendSync(args, timeout: 90)
            DispatchQueue.main.async {
                m.working = false
                if rc == 0 {
                    m.raw = out
                    m.rows = parseKV(out)
                    state.statusText = state.t("done")
                } else {
                    m.raw = err.isEmpty ? out : err
                    m.rows = [KVRow(key: "", value: m.raw, raw: true)]
                    state.statusText = state.t("failed")
                }
            }
        }
    }

    private func parseKV(_ text: String) -> [KVRow] {
        var out: [KVRow] = []
        for line in text.components(separatedBy: "\n") {
            let l = line.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !l.isEmpty else { continue }
            if let eq = l.firstIndex(of: "="), !l.hasPrefix("#"), !l.hasPrefix("PROGRESS") {
                let k = String(l[..<eq])
                if k.count <= 28 && !k.contains(" ") {
                    out.append(KVRow(key: k.trimmingCharacters(in: .whitespaces),
                                     value: String(l[l.index(after: eq)...]).trimmingCharacters(in: .whitespaces), raw: false))
                    continue
                }
            }
            out.append(KVRow(key: "", value: l, raw: true))
        }
        return out
    }
}

// MARK: - PKG files

struct FileRow: Identifiable, Hashable {
    let id = UUID()
    let path: String
    let size: String
    let state: String
}

func fmtSize(_ bytes: Int64) -> String {
    if bytes >= 1_048_576 { return String(format: "%.2f MiB", Double(bytes) / 1048576.0) }
    if bytes >= 1024 { return String(format: "%.1f KiB", Double(bytes) / 1024.0) }
    return "\(bytes) B"
}

struct FilesView: View {
    let state: AppState
    private var m: FilesModel { state.files }

    private var shown: [FileRow] {
        let q = m.filter.lowercased()
        let base = q.isEmpty ? m.all : m.all.filter { $0.path.lowercased().contains(q) }
        return Array(base.prefix(5000))
    }

    var body: some View {
        PageHeader(kicker: state.t("files_kicker"), title: state.t("files_title"), desc: state.t("files_desc"), dark: state.dark)
        Card(dark: state.dark) {
            VStack(alignment: .leading, spacing: 8) {
                FieldLabel(text: state.t("pkg_label"), dark: state.dark)
                HStack {
                    TextField(state.t("pkg_ph"), text: bind({ m.pkg }, { m.pkg = $0 }))
                        .dsField(state.dark)
                    Button(state.t("btn_choose")) {
                        if let p = pickFile(title: state.t("btn_choose"), extensions: ["pkg"]) { m.pkg = p }
                    }.buttonStyle(DSGhost(dark: state.dark))
                }
                HStack {
                    Text(state.t("passcode_label")).font(.system(size: 13, weight: .semibold))
                    SecureField("", text: bind({ m.passcode }, { m.passcode = $0 }))
                        .dsField(state.dark).frame(width: 280)
                    Button(state.t("btn_list")) { list() }.buttonStyle(DSPillPrimary(dark: state.dark))
                        .buttonStyle(DSPillPrimary(dark: state.dark)).disabled(m.working || state.progActive)
                    Spacer()
                    TextField(state.t("filter_ph"), text: bind({ m.filter }, { m.filter = $0 }))
                        .dsField(state.dark).frame(width: 220)
                }.padding(.top, 4)
            }
        }
        Card(dark: state.dark) {
            Table(shown, selection: bind({ m.sel }, { m.sel = $0 })) {
                TableColumn(state.t("col_path"), value: \.path).width(min: 280, ideal: 460)
                TableColumn(state.t("col_size"), value: \.size).width(130)
                TableColumn(state.t("col_state"), value: \.state).width(140)
            }
            .frame(minHeight: 220)
        }
        HStack {
            Button(state.t("btn_extract_sel")) { extractOne() }.buttonStyle(DSGhost(dark: state.dark)).disabled(state.progActive)
            Button(state.t("btn_extract_all")) { extractAll() }.buttonStyle(DSPillPrimary(dark: state.dark)).disabled(state.progActive)
            Spacer()
            Text(state.t("count_files", ["shown": "\(shown.count)", "total": "\(m.all.count)"]))
                .font(.system(size: 11)).foregroundColor(.secondary)
        }
        .alert(state.t("err_title"), isPresented: bind({ m.showAlert }, { m.showAlert = $0 })) {
            Button("OK", role: .cancel) {}
        } message: { Text(m.alertMsg) }
    }

    private func currentPass() -> String {
        m.passcode.isEmpty ? String(repeating: "0", count: 32) : m.passcode
    }

    private func list() {
        guard !m.pkg.isEmpty, FileManager.default.fileExists(atPath: m.pkg) else {
            m.alertMsg = state.t("ask_pkg"); m.showAlert = true; return
        }
        m.working = true
        state.statusText = state.t("working")
        let args = ["list-pkg", m.pkg, currentPass()]
        DispatchQueue.global(qos: .userInitiated).async {
            let (rc, out, err) = runBackendSync(args, timeout: 300)
            DispatchQueue.main.async {
                m.working = false
                if rc == 0 {
                    var rows: [FileRow] = []
                    for line in out.components(separatedBy: "\n") {
                        if line.hasPrefix("#") || line.isEmpty { continue }
                        let p = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
                        guard p.count >= 2 else { continue }
                        let sz = Int64(p[1]) ?? -1
                        rows.append(FileRow(path: p[0], size: sz >= 0 ? fmtSize(sz) : p[1],
                                            state: p.count > 2 ? p[2] : ""))
                    }
                    m.all = rows
                    state.statusText = "\(rows.count)"
                } else {
                    m.alertMsg = String((err.isEmpty ? out : err).prefix(2000))
                    m.showAlert = true
                    state.statusText = state.t("failed")
                }
            }
        }
    }

    private func extractOne() {
        guard let id = m.sel, let row = m.all.first(where: { $0.id == id }) else {
            m.alertMsg = state.t("ask_select"); m.showAlert = true; return
        }
        let p = NSSavePanel()
        p.nameFieldStringValue = (row.path as NSString).lastPathComponent
        guard p.runModal() == .OK, let dest = p.url?.path else { return }
        _ = startTask(state, ["extract-file", m.pkg, row.path, dest, currentPass()],
                      status: state.t("working")) { rc, transcript in
            if rc == 0 {
                state.statusText = state.t("done")
            } else {
                m.alertMsg = String(transcript.suffix(2000)); m.showAlert = true
                state.statusText = state.t("failed")
            }
        }
    }

    private func extractAll() {
        guard !m.pkg.isEmpty else {
            m.alertMsg = state.t("ask_pkg"); m.showAlert = true; return
        }
        guard let parent = pickFolder(title: state.t("btn_extract_all")) else { return }
        let base = ((m.pkg as NSString).deletingPathExtension as NSString).lastPathComponent + "-extracted"
        let dest = (parent as NSString).appendingPathComponent(base)
        if FileManager.default.fileExists(atPath: dest) {
            m.alertMsg = dest; m.showAlert = true; return
        }
        _ = startTask(state, ["extract-all", m.pkg, dest, currentPass()],
                      status: state.t("working")) { rc, transcript in
            if rc == 0 {
                state.statusText = state.t("done")
            } else {
                m.alertMsg = String(transcript.suffix(3000)); m.showAlert = true
                state.statusText = state.t("failed")
            }
        }
    }
}

// MARK: - Build PKG

struct BuildView: View {
    let state: AppState
    private var m: BuildModel { state.build }

    var body: some View {
        PageHeader(kicker: state.t("build_kicker"), title: state.t("build_title"), desc: state.t("build_desc"), dark: state.dark)
        Card(dark: state.dark) {
            VStack(alignment: .leading, spacing: 8) {
                DSSegmented(dark: state.dark,
                            options: [(false, state.t("src_folder")), (true, state.t("src_exfat"))],
                            selection: bind({ m.isImage }, { m.isImage = $0 }))
                    .frame(width: 340)
                FieldLabel(text: state.t("source_label"), dark: state.dark)
                HStack {
                    TextField(".exfat…", text: bind({ m.source }, { m.source = $0 }))
                        .dsField(state.dark)
                    Button(state.t("btn_browse")) { browseSrc() }.buttonStyle(DSGhost(dark: state.dark))
                }
                FieldLabel(text: state.t("out_label"), dark: state.dark).padding(.top, 6)
                HStack {
                    TextField("game.pkg", text: bind({ m.output }, { m.output = $0 }))
                        .dsField(state.dark)
                    Button(state.t("btn_browse")) {
                        if let p = pickSavePKG(title: state.t("out_label")) { m.output = p }
                    }.buttonStyle(DSGhost(dark: state.dark))
                }
                FieldLabel(text: state.t("tmp_label"), dark: state.dark).padding(.top, 6)
                HStack {
                    TextField("", text: bind({ m.tmp }, { m.tmp = $0 })).dsField(state.dark)
                    Button(state.t("btn_browse")) {
                        if let d = pickFolder(title: state.t("tmp_label")) { m.tmp = d }
                    }.buttonStyle(DSGhost(dark: state.dark))
                }
                HStack {
                    Text(state.t("passcode_label")).font(.system(size: 13, weight: .semibold))
                    SecureField("", text: bind({ m.passcode }, { m.passcode = $0 }))
                        .dsField(state.dark).frame(width: 260)
                    Text(state.t("sdk_label")).font(.system(size: 13, weight: .semibold)).padding(.leading, 8)
                    TextField("", text: bind({ m.sdk }, { m.sdk = $0 }))
                        .dsField(state.dark).frame(width: 70)
                    Text(state.t("mode_label")).font(.system(size: 13, weight: .semibold)).padding(.leading, 8)
                    DSSegmented(dark: state.dark,
                                options: [("Application", "Application"), ("Homebrew", "Homebrew")],
                                selection: bind({ m.mode }, { m.mode = $0 }))
                        .frame(width: 280)
                }.padding(.top, 6)
                Button(state.t("btn_build")) { build() }.buttonStyle(DSPillPrimary(dark: state.dark))
                    .buttonStyle(DSPillPrimary(dark: state.dark))
                    .disabled(state.progActive)
                    .padding(.top, 6)
            }
        }
        Card(dark: state.dark) {
            VStack(alignment: .leading, spacing: 8) {
                Text(state.t("log_label")).font(.system(size: 13, weight: .semibold))
                LogView(text: m.log, dark: state.dark)
            }
        }
        .alert(state.t("err_title"), isPresented: bind({ m.showAlert }, { m.showAlert = $0 })) {
            Button("OK", role: .cancel) {}
        } message: { Text(m.alertMsg) }
        .alert("1.0.0-NG · \(MadeByLeite)", isPresented: bind({ m.showDone }, { m.showDone = $0 })) {
            Button("OK", role: .cancel) {}
        } message: { Text(m.donePath) }
    }

    private func browseSrc() {
        if m.isImage {
            if let p = pickFile(title: state.t("src_exfat"), extensions: ["exfat"]) { m.source = p }
        } else if let d = pickFolder(title: state.t("src_folder")) { m.source = d }
    }

    private func build() {
        guard !m.source.isEmpty, !m.output.isEmpty, !m.tmp.isEmpty else {
            m.alertMsg = state.t("ask_fill"); m.showAlert = true; return
        }
        let args = [(m.isImage ? "build-exfat" : "build-folder"), m.source, m.output, m.tmp,
                    m.passcode.isEmpty ? String(repeating: "0", count: 32) : m.passcode,
                    "--sdk", m.sdk.isEmpty ? "9.00" : m.sdk,
                    "--mode", m.mode]
        _ = startTask(state, args,
                      setLog: { _ in m.log = "" },
                      appendLog: { m.log += $0 + "\n"; if m.log.count > 120_000 { m.log = String(m.log.suffix(60_000)) } },
                      status: state.t("working")) { rc, transcript in
            if rc == 0 {
                m.donePath = m.output
                m.showDone = true
                state.statusText = state.t("done")
            } else {
                m.alertMsg = String(transcript.suffix(3000)); m.showAlert = true
                state.statusText = state.t("failed")
            }
            _ = transcript
        }
    }
}
