import SwiftUI

// MARK: - Extractor

struct ExtractorView: View {
    let state: AppState
    private var m: ExtractorModel { state.extractor }

    var body: some View {
        PageHeader(kicker: state.t("ext_kicker"), title: state.t("ext_title"), desc: state.t("ext_desc"), dark: state.dark)
        Card(dark: state.dark) {
            VStack(alignment: .leading, spacing: 8) {
                FieldLabel(text: state.t("ext_pkg"), dark: state.dark)
                HStack {
                    TextField(".pkg…", text: bind({ m.pkg }, { m.pkg = $0 })).dsField(state.dark)
                    Button(state.t("btn_browse")) {
                        if let p = pickFile(title: state.t("ext_pkg"), extensions: ["pkg"]) { m.pkg = p }
                    }.buttonStyle(DSGhost(dark: state.dark))
                }
                FieldLabel(text: state.t("ext_dest"), dark: state.dark).padding(.top, 6)
                HStack {
                    TextField("", text: bind({ m.dest }, { m.dest = $0 })).dsField(state.dark)
                    Button(state.t("btn_browse")) {
                        if let parent = pickFolder(title: state.t("ext_dest")) {
                            let name = ((m.pkg as NSString).deletingPathExtension as NSString).lastPathComponent
                            m.dest = (parent as NSString).appendingPathComponent(name.isEmpty ? "pkg-extracted" : name + "-extracted")
                        }
                    }.buttonStyle(DSGhost(dark: state.dark))
                }
                FieldLabel(text: state.t("ext_tmp"), dark: state.dark).padding(.top, 6)
                HStack {
                    TextField("", text: bind({ m.tmp }, { m.tmp = $0 })).dsField(state.dark)
                    Button(state.t("btn_browse")) {
                        if let d = pickFolder(title: state.t("ext_tmp")) { m.tmp = d }
                    }.buttonStyle(DSGhost(dark: state.dark))
                }
                HStack {
                    Text(state.t("passcode_label")).font(.system(size: 13, weight: .semibold))
                    SecureField("", text: bind({ m.passcode }, { m.passcode = $0 }))
                        .dsField(state.dark).frame(width: 300)
                }.padding(.top, 6)
                Button(state.t("btn_extract")) { extract() }.buttonStyle(DSPillPrimary(dark: state.dark))
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
    }

    private func extract() {
        guard !m.pkg.isEmpty, !m.dest.isEmpty, !m.tmp.isEmpty else {
            m.alertMsg = state.t("ask_fill"); m.showAlert = true; return
        }
        _ = startTask(state, ["extract-pkg-folder", m.pkg, m.dest, m.tmp,
                              m.passcode.isEmpty ? String(repeating: "0", count: 32) : m.passcode],
                      setLog: { _ in m.log = "" },
                      appendLog: { m.log += $0 + "\n"; if m.log.count > 120_000 { m.log = String(m.log.suffix(60_000)) } },
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

// MARK: - Trophies

struct TrophyRow: Identifiable, Hashable {
    let id = UUID()
    let tid: String
    let st: String
    let grade: String
    let name: String
}

struct TrophyView: View {
    let state: AppState
    private var m: TrophyModel { state.trophy }

    var body: some View {
        PageHeader(kicker: state.t("tr_kicker"), title: state.t("tr_title"), desc: state.t("tr_desc"), dark: state.dark)
        Card(dark: state.dark) {
            HStack {
                Text(state.t("tr_ip")).font(.system(size: 13, weight: .semibold))
                TextField("192.168.x.x", text: bind({ m.host }, { m.host = $0 }))
                    .dsField(state.dark).frame(width: 150)
                Button(state.t("btn_detect")) { detect() }.buttonStyle(DSGhost(dark: state.dark)).disabled(m.working || state.progActive)
                Text(state.t("tr_ppsa")).font(.system(size: 13, weight: .semibold)).padding(.leading, 8)
                TextField("PPSAxxxxx", text: bind({ m.ppsa }, { m.ppsa = $0 }))
                    .dsField(state.dark).frame(width: 130)
                Button(state.t("btn_load")) { load() }.buttonStyle(DSPillPrimary(dark: state.dark)).disabled(m.working || state.progActive)
            }
        }
        Card(dark: state.dark) {
            Table(m.rows, selection: bind({ m.sel }, { m.sel = $0 })) {
                TableColumn(state.t("col_id"), value: \.tid).width(70)
                TableColumn(state.t("col_status"), value: \.st).width(110)
                TableColumn(state.t("col_grade"), value: \.grade).width(120)
                TableColumn(state.t("col_name"), value: \.name)
            }
            .frame(minHeight: 220)
        }
        HStack {
            Button(state.t("btn_unlock_sel")) { unlockOne() }.buttonStyle(DSGhost(dark: state.dark)).disabled(state.progActive)
            Button(state.t("btn_unlock_all")) { m.confirmAll = true }.buttonStyle(DSPillPrimary(dark: state.dark)).disabled(state.progActive)
            Button(state.t("btn_refresh")) { load() }.buttonStyle(DSGhost(dark: state.dark)).disabled(m.working || state.progActive)
            Spacer()
            Text(m.info).font(.system(size: 11)).foregroundColor(.secondary)
        }
        .alert(state.t("err_title"), isPresented: bind({ m.showAlert }, { m.showAlert = $0 })) {
            Button("OK", role: .cancel) {}
        } message: { Text(m.alertMsg) }
        .confirmationDialog(state.t("btn_unlock_all"), isPresented: bind({ m.confirmAll }, { m.confirmAll = $0 })) {
            Button(state.t("btn_unlock_all"), role: .destructive) { unlockAll() }
            Button("Cancel", role: .cancel) {}
        } message: { Text(state.t("confirm_all")) }
    }

    private func needHost() -> String? {
        let h = m.host.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !h.isEmpty else {
            m.alertMsg = state.t("ask_ip"); m.showAlert = true; return nil
        }
        return h
    }

    private func detect() {
        guard let h = needHost() else { return }
        m.working = true
        state.statusText = state.t("working")
        DispatchQueue.global(qos: .userInitiated).async {
            let (rc, out, err) = runBackendSync(["trophy-detect", h], timeout: 90)
            DispatchQueue.main.async {
                m.working = false
                if rc == 0 {
                    for line in out.components(separatedBy: "\n") where line.hasPrefix("PPSA=") {
                        m.ppsa = line.dropFirst(5).trimmingCharacters(in: .whitespacesAndNewlines)
                    }
                    load()
                } else {
                    m.alertMsg = String((err.isEmpty ? out : err).prefix(2000))
                    m.showAlert = true
                    state.statusText = state.t("failed")
                }
            }
        }
    }

    private func parseList(_ out: String) -> [TrophyRow] {
        var rows: [TrophyRow] = []
        for line in out.components(separatedBy: "\n") {
            if line.hasPrefix("PPSA=") {
                m.info = line.trimmingCharacters(in: .whitespacesAndNewlines)
                continue
            }
            let p = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            if p.count >= 4, Int(p[0].trimmingCharacters(in: .whitespaces)) != nil {
                rows.append(TrophyRow(tid: p[0], st: p[1], grade: p[2], name: p[3]))
            }
        }
        return rows
    }

    private func load() {
        let h = m.host.trimmingCharacters(in: .whitespacesAndNewlines)
        let p = m.ppsa.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        guard !h.isEmpty, !p.isEmpty else {
            m.alertMsg = state.t("ask_ip_ppsa"); m.showAlert = true; return
        }
        m.working = true
        state.statusText = state.t("working")
        DispatchQueue.global(qos: .userInitiated).async {
            let (rc, out, err) = runBackendSync(["trophy-list", h, p], timeout: 150)
            DispatchQueue.main.async {
                m.working = false
                if rc == 0 {
                    m.rows = parseList(out)
                    state.statusText = state.t("done")
                } else {
                    m.alertMsg = String((err.isEmpty ? out : err).prefix(3000))
                    m.showAlert = true
                    state.statusText = state.t("failed")
                }
            }
        }
    }

    private func unlockOne() {
        guard let id = m.sel, let row = m.rows.first(where: { $0.id == id }) else {
            m.alertMsg = state.t("ask_trophy"); m.showAlert = true; return
        }
        let h = m.host.trimmingCharacters(in: .whitespacesAndNewlines)
        let p = m.ppsa.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        _ = startTask(state, ["trophy-unlock", h, p, row.tid],
                      status: state.t("working")) { rc, transcript in
            if rc != 0 {
                m.alertMsg = String(transcript.suffix(3000)); m.showAlert = true
            }
            load()
        }
    }

    private func unlockAll() {
        let h = m.host.trimmingCharacters(in: .whitespacesAndNewlines)
        let p = m.ppsa.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        guard !h.isEmpty, !p.isEmpty else {
            m.alertMsg = state.t("ask_ip_ppsa"); m.showAlert = true; return
        }
        _ = startTask(state, ["trophy-unlock-all", h, p],
                      status: state.t("working")) { rc, transcript in
            if rc != 0 {
                m.alertMsg = String(transcript.suffix(3000)); m.showAlert = true
            }
            load()
        }
    }
}

// MARK: - About (Made by Leite)

struct AboutView: View {
    let state: AppState

    var body: some View {
        PageHeader(kicker: state.t("about_kicker"), title: state.t("about_title"), desc: "", dark: state.dark)
        HStack(spacing: 14) {
            MiniCard(color: .accentColor, title: state.t("mac_only_title"), text: state.t("mac_only_body"))
            MiniCard(color: .orange, title: state.t("win_only_title"), text: state.t("win_only_body"))
        }
        Card(dark: state.dark) {
            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 10) {
                    ZStack {
                        Circle().fill(Color.accentColor).frame(width: 34, height: 34)
                        Text("L").font(.system(size: 15, weight: .bold)).foregroundColor(.white)
                    }
                    VStack(alignment: .leading, spacing: 1) {
                        Text(MadeByLeite).font(.system(size: 14, weight: .bold))
                        Text("PS5 FPKG Builder · v\(MacPortVersion)").font(.system(size: 11)).foregroundColor(.secondary)
                    }
                }
                Text(state.lang == .ptBR ? AboutPT : AboutEN)
                    .font(.system(size: 12))
                    .textSelection(.enabled)
            }
        }
    }
}

struct MiniCard: View {
    let color: Color
    let title: String
    let text: String
    var body: some View {
        VStack(spacing: 0) {
            color.frame(height: 3)
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(.system(size: 13, weight: .bold))
                Text(text).font(.system(size: 11)).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            .padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .background(Color(NSColor.controlBackgroundColor))
        .cornerRadius(12)
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.primary.opacity(0.08), lineWidth: 1))
        .padding(.bottom, 14)
    }
}
