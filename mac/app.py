#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""PS5 FPKG Builder — macOS (port r30) — professional UI v3.

Sun Valley ttk theme (vendored in sv_ttk/, MIT by rdbende) + custom
sidebar/cards design. Languages: PT-BR / EN. Themes: light / dark.
"""
import json
import os
import queue
import subprocess
import threading
import time
import tkinter as tk
import tkinter.font as tkfont
from collections import deque
from tkinter import ttk, filedialog, messagebox

APP_VERSION = "1.4.5-mac-r30-v3"
ACCENT = "#0A84FF"

# ------------------------------------------------------------- backend
def _base_dir():
    return os.path.dirname(os.path.abspath(__file__))

def find_backend():
    base = _base_dir()
    for c in (
        os.path.join(base, "FpkgMac", "FpkgMac"),
        os.path.join(base, "FpkgMac", "FpkgMac.dll"),
        os.path.join(base, "publish-arm64", "FpkgMac"),
        os.path.join(base, "publish-arm64", "FpkgMac.dll"),
        os.path.join(base, "FpkgMac", "bin", "Release", "net10.0", "FpkgMac.dll"),
        os.path.join(base, "..", "FpkgMac", "bin", "Release", "net10.0", "FpkgMac.dll"),
    ):
        if os.path.exists(os.path.abspath(c)):
            return os.path.abspath(c)
    return None

BACKEND = find_backend()

def run_backend(args, timeout=180):
    be = BACKEND
    if not be or not os.path.exists(be):
        return (127, "", "Backend FpkgMac not found.")
    cmd = (["dotnet", be] if be.endswith(".dll") else [be]) + list(args)
    try:
        # timeout<=0 means: no timeout (long builds).
        p = subprocess.run(cmd, capture_output=True, text=True,
                           timeout=(timeout if timeout and timeout > 0 else None))
        return (p.returncode, p.stdout or "", p.stderr or "")
    except subprocess.TimeoutExpired:
        return (124, "", "Timed out.")
    except FileNotFoundError as e:
        return (127, "", str(e))
    except Exception as e:
        return (1, "", str(e))

def find_sv_theme():
    base = _base_dir()
    for c in (os.path.join(base, "sv_ttk", "sv.tcl"),
              os.path.join(base, "..", "sv_ttk", "sv.tcl")):
        if os.path.exists(c):
            return os.path.abspath(c)
    return None

# ------------------------------------------------------------- settings
def settings_path():
    try:
        d = os.path.join(os.path.expanduser("~"), "Library", "Application Support", "PS5 FPKG Builder")
        os.makedirs(d, exist_ok=True)
        return os.path.join(d, "settings.json")
    except Exception:
        return os.path.join(os.path.expanduser("~"), ".ps5-fpkg-mac.json")

def load_settings():
    try:
        with open(settings_path(), "r", encoding="utf-8") as f:
            s = json.load(f)
            lang = s.get("lang", "pt-BR")
            theme = s.get("theme", "light")
            return {"lang": lang if lang in STRINGS else "pt-BR",
                    "theme": theme if theme in ("light", "dark") else "light"}
    except Exception:
        return {"lang": "pt-BR", "theme": "light"}

def save_settings(lang, theme):
    try:
        with open(settings_path(), "w", encoding="utf-8") as f:
            json.dump({"lang": lang, "theme": theme}, f)
    except Exception:
        pass

# ------------------------------------------------------------- i18n
STRINGS = {
"en": {
    "version_tag": "macOS port · r30",
    "backend_checking": "checking backend…",
    "backend_ok": "Backend ready",
    "backend_fail": "Backend failed",
    "ready": "Ready.",
    "nav_reader": "Reader",
    "nav_files": "PKG Files",
    "nav_build": "Build PKG",
    "nav_extractor": "Extractor",
    "nav_trophy": "Trophies",
    "nav_about": "About",
    "to_light": "Light",
    "to_dark": "Dark",
    "reader_kicker": "INSPECT",
    "reader_title": "Package reader",
    "reader_desc": "Inspect a prepared folder (with sce_sys/param.json) or a readable debug PKG. Encrypted metadata needs the 32-character passcode.",
    "source_label": "Source",
    "source_ph": "Choose a game folder or a .pkg file…",
    "btn_folder": "Folder",
    "btn_pkg": "PKG file",
    "passcode_label": "Passcode",
    "btn_inspect": "Inspect",
    "btn_clear": "Clear",
    "btn_copy": "Copy",
    "result_label": "Metadata",
    "result_empty": "Nothing inspected yet. Choose a source and press Inspect.",
    "files_kicker": "BROWSE",
    "files_title": "PKG file browser",
    "files_desc": "List the inner files of a debug FIH/PPR package without extracting everything, then extract a single file or the whole content.",
    "pkg_label": "Package",
    "pkg_ph": "Choose a .pkg file…",
    "btn_choose": "Choose",
    "btn_list": "List files",
    "filter_ph": "Filter files…",
    "col_path": "Path",
    "col_size": "Size",
    "col_state": "State",
    "btn_extract_sel": "Extract selected",
    "btn_extract_all": "Extract all",
    "count_files": "{shown} of {total} files",
    "build_kicker": "BUILD",
    "build_title": "Build a debug PKG",
    "build_desc": "From a prepared folder or a raw exFAT dump. exFAT is staged through a temporary copy because macOS cannot mount raw dumps. Output is a FullDebug FIH package (BuiltIn Kraken).",
    "src_folder": "Folder",
    "src_exfat": "exFAT image",
    "build_src_ph": "Choose a game folder or an .exfat image…",
    "build_out_ph": "New .pkg file…",
    "build_tmp_ph": "Temporary folder…",
    "out_label": "Output PKG",
    "tmp_label": "Temporary",
    "sdk_label": "SDK",
    "mode_label": "Mode",
    "btn_build": "Build PKG",
    "build_done": "PKG created:\n{path}",
    "ext_kicker": "EXPORT",
    "ext_title": "PKG to folder",
    "ext_desc": "Export readable package content to a normal folder, without rebuilding. The original PKG is opened read-only and never modified.",
    "ext_pkg": "PKG file",
    "ext_dest": "Destination",
    "ext_dest_ph": "A new folder will be created…",
    "ext_tmp": "Temporary",
    "btn_browse": "Browse",
    "btn_extract": "Extract to folder",
    "log_label": "Log",
    "tr_kicker": "CONSOLE",
    "tr_title": "Trophy unlocker",
    "tr_desc": "Works over FTP (port 2121) with the payload loader on port 9021. No external ELF needed — the payload is embedded.",
    "tr_ip": "PS5 IP address",
    "btn_detect": "Detect game",
    "tr_ppsa": "Title ID",
    "btn_load": "Load trophies",
    "col_id": "ID",
    "col_status": "Status",
    "col_grade": "Grade",
    "col_name": "Trophy",
    "btn_unlock_sel": "Unlock selected",
    "btn_unlock_all": "Unlock all",
    "btn_refresh": "Refresh",
    "about_kicker": "INFO",
    "about_title": "About this Mac port",
    "working": "Working…",
    "transferred": "{pct}% · {done} of {total} MB · {speed} MB/s · {left} MB left{eta}",
    "done": "Done.",
    "failed": "Failed.",
    "inspecting": "Inspecting package…",
    "listing": "Indexing PKG files…",
    "listed": "{n} files indexed — the PKG was not fully extracted.",
    "extracting_one": "Extracting {name}…",
    "extracted_to": "Extracted to:\n{path}",
    "extracting_all": "Extracting everything…",
    "extract_done": "Extraction finished.",
    "loading_trophies": "Reading trophy list…",
    "trophy_loaded": "{n} trophies loaded.",
    "detecting": "Detecting active game…",
    "unlocking": "Unlocking trophy {tid}…",
    "unlock_all": "Unlocking all trophies…",
    "warn_title": "Attention",    "err_title": "Error",
    "info_title": "Done",
    "ask_pkg": "Choose a valid .pkg file first.",
    "ask_src": "Choose a game folder or a .pkg file first.",
    "ask_select": "Select a file in the list first.",
    "ask_fill": "Fill in the PKG, destination and temporary folder.",
    "ask_ip": "Enter the PS5 IP address.",
    "ask_ip_ppsa": "The IP address and title ID are required.",
    "ask_trophy": "Select a trophy first.",
    "confirm_all": "Unlock ALL listed trophies?",
    "extract_ok": "Content extracted to:\n{path}",
    "new_folder_name": "New folder name:",
    "save_as": "Save “{name}” as…",
    "choose_parent": "Choose the parent folder",
    "backend_missing": "FpkgMac backend not found. Please reinstall the app.",
    "mac_only_title": "Runs on Mac",
    "mac_only_body": "Reader · file browser · extraction · PKG-to-folder · PKG build (folder/exFAT, BuiltIn Kraken) · trophy unlocker",
    "win_only_title": "Still Windows-only",
    "win_only_body": "Delta/backport builds (Sony SDK) · zero-copy exFAT mount · FFPKG/UFS2 tools",
},
"pt-BR": {
    "version_tag": "porte macOS · r30",
    "backend_checking": "verificando backend…",
    "backend_ok": "Backend pronto",
    "backend_fail": "Backend falhou",
    "ready": "Pronto.",
    "nav_reader": "Leitor",
    "nav_files": "Arquivos do PKG",
    "nav_build": "Criar PKG",
    "nav_extractor": "Extrator",
    "nav_trophy": "Troféus",
    "nav_about": "Sobre",
    "to_light": "Claro",
    "to_dark": "Escuro",
    "reader_kicker": "INSPECIONAR",
    "reader_title": "Leitor de pacote",
    "reader_desc": "Inspecione uma pasta preparada (com sce_sys/param.json) ou um PKG debug legível. Metadados cifrados pedem o passcode de 32 caracteres.",
    "source_label": "Fonte",
    "source_ph": "Escolha uma pasta de jogo ou um arquivo .pkg…",
    "btn_folder": "Pasta",
    "btn_pkg": "Arquivo PKG",
    "passcode_label": "Passcode",
    "btn_inspect": "Inspecionar",
    "btn_clear": "Limpar",
    "btn_copy": "Copiar",
    "result_label": "Metadados",
    "result_empty": "Nada inspecionado ainda. Escolha uma fonte e clique em Inspecionar.",
    "files_kicker": "NAVEGAR",
    "files_title": "Navegador de arquivos do PKG",
    "files_desc": "Liste os arquivos internos de um pacote debug FIH/PPR sem extrair tudo, depois extraia um arquivo ou o conteúdo inteiro.",
    "pkg_label": "Pacote",
    "pkg_ph": "Escolha um arquivo .pkg…",
    "btn_choose": "Escolher",
    "btn_list": "Listar arquivos",
    "filter_ph": "Filtrar arquivos…",
    "col_path": "Caminho",
    "col_size": "Tamanho",
    "col_state": "Estado",
    "btn_extract_sel": "Extrair selecionado",
    "btn_extract_all": "Extrair tudo",
    "count_files": "{shown} de {total} arquivos",
    "build_kicker": "CRIAR",
    "build_title": "Criar um PKG debug",
    "build_desc": "De uma pasta preparada ou de um dump exFAT cru. O exFAT passa por uma cópia temporária porque o macOS não monta dumps crus. A saída é um pacote FullDebug FIH (Kraken BuiltIn).",
    "src_folder": "Pasta",
    "src_exfat": "Imagem exFAT",
    "build_src_ph": "Escolha uma pasta de jogo ou uma imagem .exfat…",
    "build_out_ph": "Novo arquivo .pkg…",
    "build_tmp_ph": "Pasta temporária…",
    "out_label": "PKG de saída",
    "tmp_label": "Temporária",
    "sdk_label": "SDK",
    "mode_label": "Modo",
    "btn_build": "Criar PKG",
    "build_done": "PKG criado:\n{path}",
    "ext_kicker": "EXPORTAR",
    "ext_title": "PKG para pasta",
    "ext_desc": "Exporte o conteúdo legível do pacote para uma pasta normal, sem rebuild. O PKG original é aberto somente-leitura e nunca é modificado.",
    "ext_pkg": "Arquivo PKG",
    "ext_dest": "Destino",
    "ext_dest_ph": "Uma nova pasta será criada…",
    "ext_tmp": "Temporária",
    "btn_browse": "Procurar",
    "btn_extract": "Extrair para a pasta",
    "log_label": "Registro",
    "tr_kicker": "CONSOLE",
    "tr_title": "Desbloqueador de troféus",
    "tr_desc": "Funciona via FTP (porta 2121) com o payload loader na porta 9021. Sem ELF externo — o payload já vem embutido.",
    "tr_ip": "IP do PS5",
    "btn_detect": "Detectar jogo",
    "tr_ppsa": "Title ID",
    "btn_load": "Carregar troféus",
    "col_id": "ID",
    "col_status": "Estado",
    "col_grade": "Nível",
    "col_name": "Troféu",
    "btn_unlock_sel": "Desbloquear selecionado",
    "btn_unlock_all": "Desbloquear todos",
    "btn_refresh": "Atualizar",
    "about_kicker": "INFORMAÇÃO",
    "about_title": "Sobre este porte para Mac",
    "working": "Trabalhando…",
    "transferred": "{pct}% · {done} de {total} MB · {speed} MB/s · faltam {left} MB{eta}",
    "done": "Concluído.",
    "failed": "Falhou.",
    "inspecting": "Inspecionando pacote…",
    "listing": "Indexando arquivos do PKG…",
    "listed": "{n} arquivos indexados — o PKG não foi extraído por inteiro.",
    "extracting_one": "Extraindo {name}…",
    "extracted_to": "Extraído para:\n{path}",
    "extracting_all": "Extraindo tudo…",
    "extract_done": "Extração concluída.",
    "loading_trophies": "Lendo lista de troféus…",
    "trophy_loaded": "{n} troféus carregados.",
    "detecting": "Detectando jogo ativo…",
    "unlocking": "Desbloqueando troféu {tid}…",
    "unlock_all": "Desbloqueando todos os troféus…",
    "warn_title": "Atenção",
    "err_title": "Erro",
    "info_title": "Concluído",
    "ask_pkg": "Escolha primeiro um arquivo .pkg válido.",
    "ask_src": "Escolha primeiro uma pasta de jogo ou um arquivo .pkg.",
    "ask_select": "Selecione primeiro um arquivo na lista.",
    "ask_fill": "Preencha o PKG, o destino e a pasta temporária.",
    "ask_ip": "Digite o IP do PS5.",
    "ask_ip_ppsa": "O IP e o Title ID são obrigatórios.",
    "ask_trophy": "Selecione primeiro um troféu.",
    "confirm_all": "Desbloquear TODOS os troféus listados?",
    "extract_ok": "Conteúdo extraído para:\n{path}",
    "new_folder_name": "Nome da nova pasta:",
    "save_as": "Salvar “{name}” como…",
    "choose_parent": "Escolha a pasta pai",
    "backend_missing": "Backend FpkgMac não encontrado. Reinstale o app.",
    "mac_only_title": "Funciona no Mac",
    "mac_only_body": "Leitor · navegador de arquivos · extração · PKG-para-pasta · criação de PKG (pasta/exFAT, Kraken BuiltIn) · troféus",
    "win_only_title": "Ainda exige Windows",
    "win_only_body": "Updates delta/backport (SDK Sony) · mount exFAT zero-copy · ferramentas FFPKG/UFS2",
},
}

ABOUT_EN = """The original tool is a WinForms + .NET 9 Windows application that depends on OSFMount (exFAT mounting, Windows admin), prospero-pub-cmd.exe from the Sony SDK and libScePubTools x64 + UFS2Tool/Dokan.

None of that exists on macOS, so this port reuses the portable LibProsperoPkg core. Reader, extraction and trophy unlocking work as-is. Debug PKG building also works on Mac: folders and raw exFAT dumps build through the managed BuiltIn Kraken backend (the native Oodle path needs 64-bit Windows). Raw exFAT dumps are staged through a temporary copy because stock macOS cannot mount them.

Still Windows-only: delta/backport updates (Sony SDK), zero-copy exFAT mount, FFPKG/UFS2 tools.

Original credits: Zer0Day · Drakmor / ShadowMountPlus · svenGDK (LibProsperoPKG, UFS2Tool) · EchoStretch / cragson (A53) · kerrdec97 (ps5-exfat-builder) · MkPFS / Lazy_MkPFS. Interface theme: Sun Valley by rdbende (MIT)."""

ABOUT_PT = """A ferramenta original é um aplicativo WinForms + .NET 9 para Windows que depende do OSFMount (montar exFAT, admin no Windows), do prospero-pub-cmd.exe do SDK da Sony e do libScePubTools x64 + UFS2Tool/Dokan.

Nada disso existe no macOS, então este porte reaproveita o núcleo portável LibProsperoPkg. Leitor, extração e troféus funcionam direto. A criação de PKG debug também funciona no Mac: pastas e dumps exFAT crus compilam pelo backend gerenciado BuiltIn Kraken (o caminho Oodle nativo exige Windows 64-bit). Dumps exFAT crus passam por uma cópia temporária porque o macOS não os monta.

Ainda exige Windows: updates delta/backport (SDK Sony), mount exFAT zero-copy, ferramentas FFPKG/UFS2.

Créditos originais: Zer0Day · Drakmor / ShadowMountPlus · svenGDK (LibProsperoPKG, UFS2Tool) · EchoStretch / cragson (A53) · kerrdec97 (ps5-exfat-builder) · MkPFS / Lazy_MkPFS. Tema da interface: Sun Valley por rdbende (MIT)."""

# ------------------------------------------------------------- app
class App(tk.Tk):
    def __init__(self):
        super().__init__()
        s = load_settings()
        self.lang = s["lang"]
        self.mode = s["theme"]
        self.title("PS5 FPKG Builder")
        self.geometry("1120x720")
        self.minsize(1020, 640)
        self.pkg_files = []
        self.current_pkg = ""
        self.current_pass = "0" * 32
        self.pages = {}
        self.nav_items = {}
        self.current_page = "reader"
        self.busy = False
        self._sv_ok = self._load_sv_theme()
        self.style = ttk.Style(self)
        self._setup_fonts()
        self._build_shell()
        self._build_all_pages()
        self.apply_mode()
        self.apply_language(initial=True)
        self._check_backend()

    # ---------------- theme load
    def _load_sv_theme(self):
        p = find_sv_theme()
        if not p:
            return False
        try:
            self.tk.call("source", p)
            return True
        except Exception:
            return False

    def _setup_fonts(self):
        def pick(*names):
            avail = set(tkfont.names()) | set()
            for n in names:
                try:
                    tkfont.Font(family=n, size=12)
                    return n
                except Exception:
                    continue
            return "TkDefaultFont"
        # On macOS these exist; fall back gracefully elsewhere.
        self.F_H1 = ("SF Pro Display", 22, "bold")
        self.F_H2 = ("SF Pro Text", 13, "bold")
        self.F_BODY = ("SF Pro Text", 12)
        self.F_SMALL = ("SF Pro Text", 11)
        self.F_MUTED = ("SF Pro Text", 11)
        self.F_KICKER = ("SF Pro Text", 10, "bold")
        self.F_MONO = ("SF Mono", 11)
        self.F_NAV = ("SF Pro Text", 13)
        try:
            tkfont.Font(family="SF Pro Display", size=22, weight="bold")
        except Exception:
            self.F_H1 = ("Helvetica Neue", 22, "bold")
            self.F_H2 = ("Helvetica Neue", 13, "bold")
            self.F_BODY = ("Helvetica Neue", 12)
            self.F_SMALL = ("Helvetica Neue", 11)
            self.F_MUTED = ("Helvetica Neue", 11)
            self.F_KICKER = ("Helvetica Neue", 10, "bold")
            self.F_NAV = ("Helvetica Neue", 13)
            try:
                tkfont.Font(family="Menlo", size=11)
                self.F_MONO = ("Menlo", 11)
            except Exception:
                pass

    # ---------------- i18n
    def t(self, key, **kw):
        d = STRINGS.get(self.lang, STRINGS["en"])
        v = d.get(key, STRINGS["en"].get(key, key))
        if isinstance(v, str) and kw:
            try:
                return v.format(**kw)
            except Exception:
                return v
        return v if isinstance(v, str) else key

    # ---------------- shell
    def _build_shell(self):
        self.shell = ttk.Frame(self)
        self.shell.pack(fill="both", expand=True)
        # sidebar
        self.side = tk.Frame(self.shell, width=232, highlightthickness=0, bd=0)
        self.side.pack(side="left", fill="y")
        self.side.pack_propagate(False)
        # brand
        brand = tk.Frame(self.side, bd=0, highlightthickness=0)
        brand.pack(fill="x", padx=18, pady=(20, 6))
        self.badge = tk.Canvas(brand, width=40, height=40, bd=0, highlightthickness=0)
        self.badge.pack(side="left")
        btxt = tk.Frame(brand, bd=0, highlightthickness=0)
        btxt.pack(side="left", padx=(10, 0))
        self.brand_name = tk.Label(btxt, text="PS5 FPKG\nBuilder", justify="left",
                                   font=("SF Pro Display", 14, "bold"), bd=0)
        self.brand_name.pack(anchor="w")
        self.brand_ver = tk.Label(btxt, text="v1.4.5 · " + self.t("version_tag"),
                                  font=("SF Pro Text", 10), bd=0)
        self.brand_ver.pack(anchor="w")
        # nav
        navwrap = tk.Frame(self.side, bd=0, highlightthickness=0)
        navwrap.pack(fill="x", padx=12, pady=(14, 0))
        for key in ("reader", "files", "build", "extractor", "trophy", "about"):
            item = tk.Frame(navwrap, bd=0, highlightthickness=0)
            item.pack(fill="x", pady=2)
            bar = tk.Frame(item, width=3, bd=0, highlightthickness=0)
            bar.pack(side="left", fill="y", padx=(2, 0))
            btn = tk.Button(item, font=self.F_NAV, relief="flat", anchor="w",
                            bd=0, highlightthickness=0, padx=12, pady=9, cursor="hand2",
                            command=lambda k=key: self.show_page(k))
            btn.pack(side="left", fill="x", expand=True)
            btn.bind("<Enter>", lambda e, k=key: self._nav_hover(k, True))
            btn.bind("<Leave>", lambda e, k=key: self._nav_hover(k, False))
            self.nav_items[key] = (item, bar, btn)
        # bottom controls
        bottom = tk.Frame(self.side, bd=0, highlightthickness=0)
        bottom.pack(side="bottom", fill="x", padx=14, pady=14)
        self.seg = tk.Frame(bottom, bd=0, highlightthickness=1)
        self.seg.pack(fill="x", pady=(0, 8))
        self.seg_pt = tk.Button(self.seg, text="PT-BR", font=("SF Pro Text", 11, "bold"),
                                relief="flat", bd=0, highlightthickness=0, pady=5, cursor="hand2",
                                command=lambda: self.set_lang("pt-BR"))
        self.seg_en = tk.Button(self.seg, text="EN", font=("SF Pro Text", 11, "bold"),
                                relief="flat", bd=0, highlightthickness=0, pady=5, cursor="hand2",
                                command=lambda: self.set_lang("en"))
        self.seg_pt.pack(side="left", fill="x", expand=True)
        self.seg_en.pack(side="left", fill="x", expand=True)
        self.theme_btn = ttk.Button(bottom, command=self.toggle_mode)
        self.theme_btn.pack(fill="x")
        # main column
        main = ttk.Frame(self.shell)
        main.pack(side="left", fill="both", expand=True)
        self.content = ttk.Frame(main)
        self.content.pack(fill="both", expand=True, padx=26, pady=(20, 8))
        # status bar
        sep = ttk.Separator(main, orient="horizontal")
        sep.pack(fill="x")
        status = ttk.Frame(main, padding=(26, 8))
        status.pack(fill="x")
        self.status_var = tk.StringVar(value="")
        ttk.Label(status, textvariable=self.status_var, font=self.F_SMALL).pack(side="left")
        self.prog_label_var = tk.StringVar(value="")
        ttk.Label(status, textvariable=self.prog_label_var,
                  font=("SF Pro Text", 10)).pack(side="right", padx=(10, 0))
        self.prog = ttk.Progressbar(status, mode="indeterminate", length=150)
        self.prog.pack(side="right", padx=(10, 0))
        self.pill = tk.Label(status, font=("SF Pro Text", 11, "bold"), bd=0, padx=10, pady=3)
        self.pill.pack(side="right")
        self.task = None

    def _nav_hover(self, key, inside):
        if key == self.current_page:
            return
        _item, _bar, btn = self.nav_items[key]
        c = self._colors()
        btn.configure(bg=c["nav_hover"] if inside else c["side"])

    # ---------------- colors
    def _colors(self):
        if self.mode == "dark":
            return {"side": "#232326", "card": "#2C2C30", "page": "#1E1E21",
                    "fg": "#F5F5F7", "muted": "#A7A7AE", "border": "#3A3A40",
                    "nav_fg": "#E8E8EC", "nav_hover": "#2E2E33", "field": "#38383E",
                    "zebra": "#333338", "pill_ok_bg": "#1D4A33", "pill_ok_fg": "#8FE3AE",
                    "pill_bad_bg": "#542227", "pill_bad_fg": "#FFA3AB"}
        return {"side": "#EFEFF3", "card": "#FFFFFF", "page": "#F5F5F8",
                "fg": "#1D1D1F", "muted": "#6E6E73", "border": "#E2E2E8",
                "nav_fg": "#1D1D1F", "nav_hover": "#E4E4EA", "field": "#FFFFFF",
                "zebra": "#F4F4F7", "pill_ok_bg": "#DDF3E5", "pill_ok_fg": "#0B6B3C",
                "pill_bad_bg": "#FADDE0", "pill_bad_fg": "#A31225"}

    def apply_mode(self):
        if self._sv_ok:
            try:
                self.style.theme_use("sun-valley-dark" if self.mode == "dark" else "sun-valley-light")
            except Exception:
                pass
        else:
            try:
                self.style.theme_use("clam")
            except Exception:
                pass
        c = self._colors()
        self.configure(bg=c["page"])
        self.side.configure(bg=c["side"])
        for w in self.side.winfo_children():
            self._paint_side(w, c)
        # content bg
        for page in self.pages.values():
            page.configure(style="TFrame")
            self._paint_cards(page, c)
        self._paint_nav()
        self._paint_seg()
        self.badge.delete("all")
        self.badge.configure(bg=c["side"], highlightthickness=0)
        self.badge.create_oval(2, 2, 38, 38, fill=ACCENT, outline="")
        self.badge.create_text(20, 21, text="P5", fill="white",
                               font=("SF Pro Display", 13, "bold"))
        self.theme_btn.configure(text=("◐  " + self.t("to_light")) if self.mode == "dark"
                                 else ("◐  " + self.t("to_dark")))

    def _paint_side(self, w, c):
        try:
            cls = w.winfo_class()
        except Exception:
            return
        if cls in ("Frame", "Canvas", "Label", "Button"):
            try:
                if cls == "Canvas":
                    w.configure(bg=c["side"])
                elif cls == "Label":
                    w.configure(bg=c["side"], fg=c["fg"] if w not in (self.brand_ver,) else c["muted"])
                elif cls == "Frame":
                    w.configure(bg=c["side"])
            except Exception:
                pass
        if w is self.seg:
            try:
                w.configure(bg=c["side"], highlightbackground=c["border"])
            except Exception:
                pass
            return
        for ch in w.winfo_children():
            self._paint_side(ch, c)

    def _paint_cards(self, page, c):
        for w in page.winfo_children():
            self._paint_card_widget(w, c)

    def _paint_card_widget(self, w, c):
        try:
            name = w.winfo_name()
            cls = w.winfo_class()
        except Exception:
            return
        if getattr(w, "_is_card", False):
            try:
                w.configure(bg=c["card"], highlightbackground=c["border"])
            except Exception:
                pass
        if getattr(w, "_is_field_label", False):
            try:
                w.configure(bg=c["card"], fg=c["muted"])
            except Exception:
                pass
        elif cls == "Label" and isinstance(w, tk.Label):
            try:
                if w.cget("bg") not in ("", "SystemButtonFace"):
                    pass
            except Exception:
                pass
        if cls == "Text":
            try:
                w.configure(bg=c["field"], fg=c["fg"], insertbackground=c["fg"],
                            highlightbackground=c["border"], selectbackground=ACCENT,
                            selectforeground="white")
            except Exception:
                pass
        for ch in w.winfo_children():
            self._paint_card_widget(ch, c)

    # ---------------- nav / lang / mode
    def show_page(self, key):
        self.current_page = key
        for k, f in self.pages.items():
            if k == key:
                f.pack(fill="both", expand=True)
            else:
                f.pack_forget()
        self._paint_nav()

    def _paint_nav(self):
        c = self._colors()
        names = {"reader": self.t("nav_reader"), "files": self.t("nav_files"),
                 "build": self.t("nav_build"),
                 "extractor": self.t("nav_extractor"), "trophy": self.t("nav_trophy"),
                 "about": self.t("nav_about")}
        for k, (item, bar, btn) in self.nav_items.items():
            active = (k == self.current_page)
            item.configure(bg=c["side"])
            bar.configure(bg=ACCENT if active else c["side"])
            btn.configure(text="   " + names[k],
                          bg=ACCENT if active else c["side"],
                          fg="white" if active else c["nav_fg"],
                          activebackground=ACCENT if active else c["nav_hover"],
                          activeforeground="white" if active else c["nav_fg"])

    def _paint_seg(self):
        c = self._colors()
        for btn, code in ((self.seg_pt, "pt-BR"), (self.seg_en, "en")):
            active = (self.lang == code)
            btn.configure(bg=ACCENT if active else c["side"],
                          fg="white" if active else c["muted"],
                          activebackground=ACCENT if active else c["nav_hover"])

    def set_lang(self, code):
        if code in STRINGS and code != self.lang:
            self.lang = code
            save_settings(self.lang, self.mode)
            self.apply_language()

    def toggle_mode(self):
        self.mode = "dark" if self.mode == "light" else "light"
        save_settings(self.lang, self.mode)
        self.apply_mode()
        self.apply_language()

    def set_busy(self, on, msg=""):
        self.busy = on
        if msg:
            self.status_var.set(msg)
        try:
            if on:
                self.prog.configure(mode="indeterminate")
                self.prog.start(12)
            else:
                self.prog.stop()
        except Exception:
            pass

    # ---------------- live task engine (streaming backend + 0-100% progress)
    def _backend_cmd(self, args):
        be = BACKEND
        if not be or not os.path.exists(be):
            return None
        return (["dotnet", be] if be.endswith(".dll") else [be]) + list(args)

    def run_task(self, args, log_widget=None, status_msg="", on_done=None, timeout=None):
        """Run the backend with live stdout streaming.

        Backend lines shaped 'PROGRESS\\tstage\\tdone\\ttotal\\tdetail' drive the
        0-100% bar, speed and MB counters; every other line goes to log_widget.
        on_done(rc, transcript) runs on the UI thread when finished.
        """
        if self.busy:
            return False
        cmd = self._backend_cmd(args)
        if cmd is None:
            messagebox.showerror(self.t("err_title"), self.t("backend_missing"))
            return False
        try:
            proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                    text=True, bufsize=1)
        except Exception as e:
            messagebox.showerror(self.t("err_title"), str(e))
            return False
        self.busy = True
        self.task = {"proc": proc, "queue": queue.Queue(), "log": log_widget,
                     "on_done": on_done, "transcript": [], "timeout": timeout,
                     "started": time.time(), "done": 0, "total": 0, "stage": "",
                     "detail": "", "samples": deque(maxlen=40), "speed": 0.0,
                     "finished": False}
        if status_msg:
            self.status_var.set(status_msg)
        self.prog_label_var.set("")
        try:
            self.prog.stop()
        except Exception:
            pass

        def reader():
            try:
                for line in proc.stdout:
                    line = line.rstrip("\n")
                    if line.startswith("PROGRESS\t"):
                        parts = line.split("\t")
                        try:
                            self.task["queue"].put(("progress", parts[1] if len(parts) > 1 else "",
                                                    int(float(parts[2])) if len(parts) > 2 else 0,
                                                    int(float(parts[3])) if len(parts) > 3 else 0,
                                                    parts[4] if len(parts) > 4 else ""))
                        except Exception:
                            pass
                    else:
                        self.task["queue"].put(("log", line))
            except Exception:
                pass
            try:
                rc = proc.wait()
            except Exception:
                rc = -1
            try:
                self.task["queue"].put(("done", rc))
            except Exception:
                pass

        threading.Thread(target=reader, daemon=True).start()
        self.after(120, self._poll_task)
        return True

    def _poll_task(self):
        t = self.task
        if t is None or t.get("finished"):
            return
        try:
            while True:
                ev = t["queue"].get_nowait()
                if ev[0] == "progress":
                    _, stage, done, total, detail = ev
                    t["stage"], t["detail"] = stage, detail
                    if total > 0:
                        t["done"], t["total"] = max(0, done), total
                        t["samples"].append((time.time(), t["done"]))
                elif ev[0] == "log":
                    t["transcript"].append(ev[1])
                    if len(t["transcript"]) > 600:
                        del t["transcript"][:200]
                    lw = t["log"]
                    if lw is not None:
                        try:
                            lw.insert("end", ev[1] + "\n")
                            lw.see("end")
                        except Exception:
                            pass
                elif ev[0] == "done":
                    self._finish_task(ev[1])
                    return
        except queue.Empty:
            pass
        # timeout kill
        if t["timeout"] and (time.time() - t["started"]) > t["timeout"]:
            try:
                t["proc"].kill()
            except Exception:
                pass
        try:
            self._paint_progress()
        except Exception:
            pass
        self.after(120, self._poll_task)

    def _mb(self, b):
        v = b / 1048576.0
        s = "%.1f" % v
        if self.lang == "pt-BR":
            s = s.replace(".", ",")
        return s

    def _paint_progress(self):
        t = self.task
        if t is None:
            return
        now = time.time()
        # decay speed after 2s of silence
        if t["samples"] and now - t["samples"][-1][0] > 2.0:
            t["speed"] = 0.0
        elif len(t["samples"]) >= 2:
            (t0, d0), (t1, d1) = t["samples"][0], t["samples"][-1]
            if t1 - t0 >= 0.15 and d1 > d0:
                t["speed"] = (d1 - d0) / (t1 - t0)
        # prune samples older than 4s
        while len(t["samples"]) > 2 and now - t["samples"][0][0] > 4.0:
            t["samples"].popleft()
        total, done = t["total"], t["done"]
        if total > 0:
            pct = max(0, min(99, int(done * 100 / total)))
            try:
                self.prog.configure(mode="determinate", maximum=100, value=pct)
            except Exception:
                pass
            left = max(0, total - done)
            eta = ""
            if t["speed"] > 0 and left > 0:
                s = left / t["speed"]
                eta = (" · ~%ds" % int(s)) if s < 90 else (" · ~%d min" % int(round(s / 60)))
            try:
                self.prog_label_var.set(self.t("transferred", pct=pct,
                                               done=self._mb(done), total=self._mb(total),
                                               speed=self._mb(t["speed"]), left=self._mb(left), eta=eta))
            except Exception:
                pass
            self.status_var.set(t["stage"] + (" — " + t["detail"] if t["detail"] else ""))
        else:
            try:
                self.prog.configure(mode="indeterminate")
                self.prog.start(12)
            except Exception:
                pass
            if t["stage"]:
                self.status_var.set(t["stage"])

    def _finish_task(self, rc):
        t = self.task
        if t is None:
            return
        t["finished"] = True
        self.task = None
        self.busy = False
        try:
            self.prog.stop()
        except Exception:
            pass
        if rc == 0 and t["total"] > 0:
            try:
                self.prog.configure(mode="determinate", maximum=100, value=100)
            except Exception:
                pass
            try:
                self.prog_label_var.set(self.t("transferred", pct=100,
                                               done=self._mb(t["total"]), total=self._mb(t["total"]),
                                               speed=self._mb(t["speed"]), left=self._mb(0), eta=""))
            except Exception:
                pass
        elif rc != 0:
            try:
                self.prog.configure(mode="determinate", maximum=100, value=0)
            except Exception:
                pass
        cb = t["on_done"]
        transcript = "\n".join(t["transcript"])
        if cb is not None:
            try:
                cb(rc, transcript)
            except Exception:
                pass

    # ---------------- shared builders
    def _card(self, parent, pad_bottom=14):
        outer = tk.Frame(parent, bd=0, highlightthickness=1)
        outer._is_card = True
        outer.pack(fill="x", pady=(0, pad_bottom), ipadx=18, ipady=14)
        return outer

    def _page_header(self, parent):
        wrap = ttk.Frame(parent)
        wrap.pack(fill="x", pady=(2, 14))
        kicker = ttk.Label(wrap, font=self.F_KICKER, foreground=ACCENT)
        kicker.pack(anchor="w")
        title = ttk.Label(wrap, font=self.F_H1)
        title.pack(anchor="w", pady=(2, 2))
        desc = ttk.Label(wrap, font=self.F_BODY, wraplength=800)
        desc.pack(anchor="w")
        return kicker, title, desc

    def _ph_entry(self, parent, var, width, placeholder, show=None, **kw):
        e = ttk.Entry(parent, textvariable=var, width=width, show=show or "", **kw)
        e._placeholder = placeholder
        e._is_ph = True
        def on_focus_in(_ev, ee=e):
            if ee.get() == ee._placeholder:
                ee.delete(0, "end")
                try:
                    ee.configure(foreground="")
                except Exception:
                    pass
        def on_focus_out(_ev, ee=e):
            if not ee.get():
                ee.insert(0, ee._placeholder)
        e.bind("<FocusIn>", on_focus_in)
        e.bind("<FocusOut>", on_focus_out)
        if not var.get():
            e.insert(0, placeholder)
        return e

    def _get_entry_value(self, entry, var):
        v = var.get()
        if getattr(entry, "_is_ph", False) and v == getattr(entry, "_placeholder", None):
            return ""
        return v.strip()

    # ---------------- pages
    def _build_all_pages(self):
        for key in ("reader", "files", "build", "extractor", "trophy", "about"):
            f = ttk.Frame(self.content)
            self.pages[key] = f
        self._build_reader()
        self._build_files()
        self._build_build()
        self._build_extract()
        self._build_trophy()
        self._build_about()
        self.show_page("reader")

    def apply_language(self, initial=False):
        self.brand_ver.configure(text="v1.4.5 · " + self.t("version_tag"))
        self._paint_nav()
        self._paint_seg()
        self.theme_btn.configure(text=("◐  " + self.t("to_light")) if self.mode == "dark"
                                 else ("◐  " + self.t("to_dark")))
        self.status_var.set(self.t("ready"))
        self._lang_reader()
        self._lang_files()
        self._lang_build()
        self._lang_extract()
        self._lang_trophy()
        self._lang_about()

    def _check_backend(self):
        self.pill.configure(text="○ " + self.t("backend_checking"))
        def work():
            rc, out, err = run_backend(["version"], timeout=20)
            def done():
                c = self._colors()
                if rc == 0:
                    self.pill.configure(text="● " + self.t("backend_ok"),
                                        bg=c["pill_ok_bg"], fg=c["pill_ok_fg"])
                    self.status_var.set(out.strip()[:100])
                else:
                    self.pill.configure(text="● " + self.t("backend_fail"),
                                        bg=c["pill_bad_bg"], fg=c["pill_bad_fg"])
                    self.status_var.set((err or out)[:220])
            self.after(0, done)
        threading.Thread(target=work, daemon=True).start()

    # ============ READER
    def _build_reader(self):
        f = self.pages["reader"]
        self.r_kicker, self.r_title, self.r_desc = self._page_header(f)
        card = self._card(f)
        r1 = tk.Frame(card, bd=0, highlightthickness=0)
        r1.pack(fill="x", padx=0, pady=(0, 10))
        self.r_src_lbl = tk.Label(r1, font=self.F_H2, bd=0)
        self.r_src_lbl._is_field_label = True
        self.r_src_lbl.pack(anchor="w", pady=(0, 6))
        row = tk.Frame(card, bd=0, highlightthickness=0)
        row.pack(fill="x")
        self.reader_path = tk.StringVar()
        self.r_entry = self._ph_entry(row, self.reader_path, 46, "")
        self.r_entry.pack(side="left", fill="x", expand=True, padx=(0, 8))
        self.r_btn_folder = ttk.Button(row, command=self._pick_reader_folder)
        self.r_btn_folder.pack(side="left", padx=2)
        self.r_btn_pkg = ttk.Button(row, command=self._pick_reader_pkg)
        self.r_btn_pkg.pack(side="left", padx=2)
        r2 = tk.Frame(card, bd=0, highlightthickness=0)
        r2.pack(fill="x", pady=(12, 0))
        self.r_pass_lbl = tk.Label(r2, font=self.F_H2, bd=0)
        self.r_pass_lbl._is_field_label = True
        self.r_pass_lbl.pack(side="left", padx=(0, 8))
        self.reader_pass = tk.StringVar(value="0" * 32)
        ttk.Entry(r2, textvariable=self.reader_pass, width=36, show="•").pack(side="left")
        self.r_btn_go = ttk.Button(r2, style="Accent.TButton", command=self._do_inspect)
        self.r_btn_go.pack(side="left", padx=(12, 4))
        self.r_btn_clear = ttk.Button(r2, command=self._clear_reader)
        self.r_btn_clear.pack(side="left", padx=2)
        # result card
        res = self._card(f)
        res.pack(fill="both", expand=True)
        top = tk.Frame(res, bd=0, highlightthickness=0)
        top.pack(fill="x", pady=(0, 8))
        self.r_res_lbl = tk.Label(top, font=self.F_H2, bd=0)
        self.r_res_lbl.pack(side="left")
        self.r_btn_copy = ttk.Button(top, command=self._copy_reader)
        self.r_btn_copy.pack(side="right")
        self.r_empty = tk.Label(res, font=self.F_BODY, bd=0, justify="left")
        self.r_empty.pack(anchor="w", pady=6)
        self.r_rows = tk.Frame(res, bd=0, highlightthickness=0)
        self.r_rows.pack(fill="both", expand=True)
        self._reader_raw = ""

    def _lang_reader(self):
        self.r_kicker.configure(text=self.t("reader_kicker"))
        self.r_title.configure(text=self.t("reader_title"))
        self.r_desc.configure(text=self.t("reader_desc"))
        self.r_src_lbl.configure(text=self.t("source_label"))
        self.r_entry._placeholder = self.t("source_ph")
        if not self.reader_path.get():
            try:
                self.r_entry.delete(0, "end")
                self.r_entry.insert(0, self.t("source_ph"))
            except Exception:
                pass
        self.r_btn_folder.configure(text=self.t("btn_folder"))
        self.r_btn_pkg.configure(text=self.t("btn_pkg"))
        self.r_pass_lbl.configure(text=self.t("passcode_label"))
        self.r_btn_go.configure(text=self.t("btn_inspect"))
        self.r_btn_clear.configure(text=self.t("btn_clear"))
        self.r_res_lbl.configure(text=self.t("result_label"))
        self.r_btn_copy.configure(text=self.t("btn_copy"))
        if not self._reader_raw:
            self.r_empty.configure(text=self.t("result_empty"))
        self.apply_mode()

    def _pick_reader_folder(self):
        d = filedialog.askdirectory(title=self.t("btn_folder"))
        if d:
            self.reader_path.set(d)

    def _pick_reader_pkg(self):
        p = filedialog.askopenfilename(title=self.t("btn_pkg"),
                                       filetypes=[("PKG", "*.pkg"), ("All", "*.*")])
        if p:
            self.reader_path.set(p)

    def _clear_reader(self):
        self._reader_raw = ""
        for w in self.r_rows.winfo_children():
            w.destroy()
        self.r_empty.configure(text=self.t("result_empty"))
        self.r_empty.pack(anchor="w", pady=6)

    def _copy_reader(self):
        try:
            self.clipboard_clear()
            self.clipboard_append(self._reader_raw)
        except Exception:
            pass

    def _do_inspect(self):
        if self.busy:
            return
        path = self._get_entry_value(self.r_entry, self.reader_path)
        if not path:
            messagebox.showwarning(self.t("warn_title"), self.t("ask_src"))
            return
        self.set_busy(True, self.t("inspecting"))
        def work():
            if os.path.isdir(path):
                rc, out, err = run_backend(["inspect-folder", path], timeout=60)
            else:
                rc, out, err = run_backend(["inspect-pkg", path,
                                            self.reader_pass.get().strip() or "0" * 32], timeout=60)
            def done():
                self.set_busy(False)
                if rc == 0:
                    self._render_reader(out)
                    self.status_var.set(self.t("done"))
                else:
                    self._reader_raw = err or out
                    self._render_reader(self._reader_raw, failed=True)
                    self.status_var.set(self.t("failed"))
            self.after(0, done)
        threading.Thread(target=work, daemon=True).start()

    def _render_reader(self, text, failed=False):
        self._reader_raw = text
        for w in self.r_rows.winfo_children():
            w.destroy()
        c = self._colors()
        lines = [l for l in text.splitlines() if l.strip()]
        if not lines:
            self.r_empty.configure(text=self.t("result_empty"))
            self.r_empty.pack(anchor="w", pady=6)
            return
        self.r_empty.pack_forget()
        for i, line in enumerate(lines[:60]):
            if "=" in line and not line.startswith((" ", "#", "PROGRESS")) and len(line.split("=", 1)[0]) <= 28:
                k, v = line.split("=", 1)
                row = tk.Frame(self.r_rows, bd=0, highlightthickness=0, bg=c["card"])
                row.pack(fill="x", pady=1)
                tk.Label(row, text=k.strip(), font=self.F_SMALL, bd=0,
                         bg=c["card"], fg=c["muted"], width=20, anchor="e").pack(side="left")
                val = tk.Entry(row, font=self.F_BODY, bd=0, highlightthickness=0,
                               readonlybackground=c["card"], bg=c["card"], fg=c["fg"])
                val.insert(0, v.strip() or "—")
                val.configure(state="readonly")
                val.pack(side="left", fill="x", expand=True, padx=(10, 0))
            else:
                tk.Label(self.r_rows, text=line, font=self.F_MONO if not failed else self.F_BODY,
                         bd=0, bg=c["card"], fg=c["muted"], anchor="w",
                         justify="left", wraplength=760).pack(fill="x", pady=1)
        self._paint_card_widget(self.r_rows, c)

    def _paint_card_widget(self, w, c):
        try:
            cls = w.winfo_class()
        except Exception:
            return
        if getattr(w, "_is_card", False):
            try:
                w.configure(bg=c["card"], highlightbackground=c["border"])
            except Exception:
                pass
        if getattr(w, "_is_field_label", False):
            try:
                w.configure(bg=c["card"], fg=c["muted"])
            except Exception:
                pass
        elif cls == "Label" and isinstance(w, tk.Label):
            try:
                if w.master.cget("bg") == c["card"] and w.cget("fg") not in (ACCENT,):
                    w.configure(bg=c["card"], fg=c["fg"])
            except Exception:
                pass
        if cls == "Text":
            try:
                w.configure(bg=c["field"], fg=c["fg"], insertbackground=c["fg"],
                            highlightbackground=c["border"], selectbackground=ACCENT,
                            selectforeground="white")
            except Exception:
                pass
        elif cls == "Entry":
            try:
                if w.cget("state") == "readonly":
                    w.configure(readonlybackground=c["card"], bg=c["card"], fg=c["fg"])
            except Exception:
                pass
        for ch in w.winfo_children():
            self._paint_card_widget(ch, c)

    # ============ FILES

    # ============ FILES
    def _build_files(self):
        f = self.pages["files"]
        self.f_kicker, self.f_title, self.f_desc = self._page_header(f)
        card = self._card(f)
        self.f_pkg_lbl = tk.Label(card, font=self.F_H2, bd=0)
        self.f_pkg_lbl._is_field_label = True
        self.f_pkg_lbl.pack(anchor="w", pady=(0, 6))
        row = tk.Frame(card, bd=0, highlightthickness=0)
        row.pack(fill="x")
        self.files_pkg = tk.StringVar()
        self.f_entry = self._ph_entry(row, self.files_pkg, 44, "")
        self.f_entry.pack(side="left", fill="x", expand=True, padx=(0, 8))
        self.f_btn_choose = ttk.Button(row, command=self._pick_files_pkg)
        self.f_btn_choose.pack(side="left")
        row2 = tk.Frame(card, bd=0, highlightthickness=0)
        row2.pack(fill="x", pady=(12, 0))
        self.f_pass_lbl = tk.Label(row2, font=self.F_H2, bd=0)
        self.f_pass_lbl._is_field_label = True
        self.f_pass_lbl.pack(side="left", padx=(0, 8))
        self.files_pass = tk.StringVar(value="0" * 32)
        ttk.Entry(row2, textvariable=self.files_pass, width=32, show="•").pack(side="left")
        self.f_btn_list = ttk.Button(row2, style="Accent.TButton", command=self._do_list)
        self.f_btn_list.pack(side="left", padx=(12, 4))
        self.files_filter = tk.StringVar()
        self.f_filter = self._ph_entry(row2, self.files_filter, 20, "")
        self.f_filter.pack(side="right")
        self.f_filter.bind("<KeyRelease>", lambda _e: self._apply_filter())
        # list card
        lst = self._card(f)
        lst.pack(fill="both", expand=True)
        cols = ("path", "size", "state")
        self.tree = ttk.Treeview(lst, columns=cols, show="headings", height=14)
        self.tree.pack(side="left", fill="both", expand=True)
        vsb = ttk.Scrollbar(lst, orient="vertical", command=self.tree.yview)
        vsb.pack(side="right", fill="y")
        self.tree.configure(yscrollcommand=vsb.set)
        foot = ttk.Frame(f)
        foot.pack(fill="x", pady=(2, 0))
        self.f_btn_one = ttk.Button(foot, command=self._do_extract_one)
        self.f_btn_one.pack(side="left", padx=(0, 6))
        self.f_btn_all = ttk.Button(foot, style="Accent.TButton", command=self._do_extract_all)
        self.f_btn_all.pack(side="left")
        self.files_count = tk.StringVar(value="")
        ttk.Label(foot, textvariable=self.files_count, font=self.F_SMALL).pack(side="right")

    def _lang_files(self):
        self.f_kicker.configure(text=self.t("files_kicker"))
        self.f_title.configure(text=self.t("files_title"))
        self.f_desc.configure(text=self.t("files_desc"))
        self.f_pkg_lbl.configure(text=self.t("pkg_label"))
        self.f_entry._placeholder = self.t("pkg_ph")
        if not self.files_pkg.get():
            try:
                self.f_entry.delete(0, "end")
                self.f_entry.insert(0, self.t("pkg_ph"))
            except Exception:
                pass
        self.f_btn_choose.configure(text=self.t("btn_choose"))
        self.f_pass_lbl.configure(text=self.t("passcode_label"))
        self.f_btn_list.configure(text=self.t("btn_list"))
        self.f_filter._placeholder = self.t("filter_ph")
        if not self.files_filter.get():
            try:
                self.f_filter.delete(0, "end")
                self.f_filter.insert(0, self.t("filter_ph"))
            except Exception:
                pass
        self.tree.heading("path", text=self.t("col_path"))
        self.tree.heading("size", text=self.t("col_size"))
        self.tree.heading("state", text=self.t("col_state"))
        self.tree.column("path", width=520)
        self.tree.column("size", width=150, anchor="e")
        self.tree.column("state", width=150)
        self.f_btn_one.configure(text=self.t("btn_extract_sel"))
        self.f_btn_all.configure(text=self.t("btn_extract_all"))
        self._apply_filter()
        self.apply_mode()

    def _pick_files_pkg(self):
        p = filedialog.askopenfilename(title=self.t("btn_choose"), filetypes=[("PKG", "*.pkg")])
        if p:
            self.files_pkg.set(p)
            self.current_pkg = p

    def _do_list(self):
        if self.busy:
            return
        pkg = self._get_entry_value(self.f_entry, self.files_pkg)
        if not pkg or not os.path.exists(pkg):
            messagebox.showwarning(self.t("warn_title"), self.t("ask_pkg"))
            return
        self.current_pkg = pkg
        self.current_pass = self.files_pass.get().strip() or "0" * 32
        self.set_busy(True, self.t("listing"))
        for i in self.tree.get_children():
            self.tree.delete(i)
        self.pkg_files = []
        def work():
            rc, out, err = run_backend(["list-pkg", pkg, self.current_pass], timeout=300)
            def done():
                self.set_busy(False)
                if rc != 0:
                    messagebox.showerror(self.t("err_title"), (err or out)[:2000])
                    self.status_var.set(self.t("failed"))
                    return
                files = []
                for line in out.splitlines():
                    if line.startswith("#"):
                        continue
                    parts = line.split("\t")
                    if len(parts) >= 2:
                        files.append((parts[0], parts[1] if len(parts) > 1 else "",
                                      parts[2] if len(parts) > 2 else ""))
                self.pkg_files = files
                self._apply_filter()
                self.status_var.set(self.t("listed", n=len(files)))
            self.after(0, done)
        threading.Thread(target=work, daemon=True).start()

    def _filter_text(self):
        v = self.files_filter.get()
        if v == getattr(self.f_filter, "_placeholder", None):
            return ""
        return (v or "").lower()

    def _apply_filter(self):
        if not hasattr(self, "tree"):
            return
        q = self._filter_text()
        for i in self.tree.get_children():
            self.tree.delete(i)
        c = self._colors()
        n = 0
        for path, size, state in self.pkg_files:
            if q and q not in path.lower():
                continue
            try:
                sz = int(size)
                if sz >= 1048576:
                    szs = "%.2f MiB" % (sz / 1048576.0)
                elif sz >= 1024:
                    szs = "%.1f KiB" % (sz / 1024.0)
                else:
                    szs = "%d B" % sz
            except Exception:
                szs = size
            tag = "even" if n % 2 == 0 else "odd"
            self.tree.insert("", "end", values=(path, szs, state), tags=(tag,))
            n += 1
            if n >= 5000:
                break
        try:
            self.tree.tag_configure("even", background="")
            self.tree.tag_configure("odd", background=c["zebra"])
        except Exception:
            pass
        if hasattr(self, "files_count"):
            self.files_count.set(self.t("count_files", shown=n, total=len(self.pkg_files)))

    def _do_extract_one(self):
        if self.busy:
            return
        sel = self.tree.selection()
        if not sel:
            messagebox.showinfo(self.t("info_title"), self.t("ask_select"))
            return
        inner = self.tree.item(sel[0], "values")[0]
        dest = filedialog.asksaveasfilename(title=self.t("save_as", name=inner),
                                            initialfile=os.path.basename(inner))
        if not dest:
            return
        def on_done(rc, transcript):
            if rc == 0:
                messagebox.showinfo(self.t("info_title"), self.t("extracted_to", path=dest))
                self.status_var.set(self.t("done"))
            else:
                messagebox.showerror(self.t("err_title"), transcript[-2000:])
                self.status_var.set(self.t("failed"))

        # streamed with live byte progress (single file: 0 -> size)
        self.run_task(["extract-file", self.current_pkg, inner, dest, self.current_pass],
                      log_widget=None, status_msg=self.t("extracting_one", name=os.path.basename(inner)),
                      on_done=on_done, timeout=300)

    def _do_extract_all(self):
        if self.busy:
            return
        if not self.current_pkg:
            messagebox.showwarning(self.t("warn_title"), self.t("ask_pkg"))
            return
        import tkinter.simpledialog as sd
        dest = filedialog.askdirectory(title=self.t("choose_parent")) or ""
        name = sd.askstring(self.t("btn_extract_all"), self.t("new_folder_name"),
                            initialvalue=os.path.splitext(os.path.basename(self.current_pkg))[0] + "-extracted")
        if not name:
            return
        full = os.path.join(dest, name) if dest else os.path.join(os.path.expanduser("~"), "Downloads", name)

        def on_done_all(rc, transcript):
            if rc == 0:
                messagebox.showinfo(self.t("info_title"), self.t("extract_ok", path=full))
                self.status_var.set(self.t("extract_done"))
            else:
                messagebox.showerror(self.t("err_title"), transcript[-3000:])
                self.status_var.set(self.t("failed"))

        self.run_task(["extract-all", self.current_pkg, full, self.current_pass],
                      log_widget=None, status_msg=self.t("extracting_all"),
                      on_done=on_done_all, timeout=None)

    # ============ BUILD
    def _build_build(self):
        f = self.pages["build"]
        self.b_kicker, self.b_title, self.b_desc = self._page_header(f)
        card = self._card(f)
        # source type segmented
        self.build_is_image = tk.BooleanVar(value=False)
        segrow = tk.Frame(card, bd=0, highlightthickness=0)
        segrow.pack(fill="x", pady=(0, 10))
        self.b_seg = tk.Frame(segrow, bd=0, highlightthickness=1)
        self.b_seg.pack(anchor="w")
        self.b_seg_folder = tk.Button(self.b_seg, font=("SF Pro Text", 11, "bold"),
                                      relief="flat", bd=0, highlightthickness=0, padx=14, pady=6,
                                      cursor="hand2", command=lambda: self._set_build_src(False))
        self.b_seg_exfat = tk.Button(self.b_seg, font=("SF Pro Text", 11, "bold"),
                                     relief="flat", bd=0, highlightthickness=0, padx=14, pady=6,
                                     cursor="hand2", command=lambda: self._set_build_src(True))
        self.b_seg_folder.pack(side="left")
        self.b_seg_exfat.pack(side="left")
        # source row
        self.b_src_lbl = tk.Label(card, font=self.F_H2, bd=0)
        self.b_src_lbl._is_field_label = True
        self.b_src_lbl.pack(anchor="w", pady=(0, 6))
        row = tk.Frame(card, bd=0, highlightthickness=0)
        row.pack(fill="x")
        self.build_src = tk.StringVar()
        self.b_entry = self._ph_entry(row, self.build_src, 44, "")
        self.b_entry.pack(side="left", fill="x", expand=True, padx=(0, 8))
        self.b_btn_browse = ttk.Button(row, command=self._browse_build_src)
        self.b_btn_browse.pack(side="left")
        # output + temp rows
        self.b_rows = []
        self.build_out = tk.StringVar()
        self.build_tmp = tk.StringVar(value=os.path.join(os.path.expanduser("~"), "Downloads", "fpkg-tmp"))
        for key, var in (("out_label", self.build_out), ("tmp_label", self.build_tmp)):
            lbl = tk.Label(card, font=self.F_H2, bd=0)
            lbl._is_field_label = True
            lbl.pack(anchor="w", pady=(10, 6))
            r2 = tk.Frame(card, bd=0, highlightthickness=0)
            r2.pack(fill="x")
            e = ttk.Entry(r2, textvariable=var, width=48)
            e.pack(side="left", fill="x", expand=True, padx=(0, 8))
            btn = ttk.Button(r2, width=10, command=lambda k=key: self._browse_build_out(k))
            btn.pack(side="left")
            self.b_rows.append((key, lbl, e, btn))
        # options row
        opt = tk.Frame(card, bd=0, highlightthickness=0)
        opt.pack(fill="x", pady=(12, 0))
        self.b_pass_lbl = tk.Label(opt, font=self.F_H2, bd=0)
        self.b_pass_lbl._is_field_label = True
        self.b_pass_lbl.pack(side="left", padx=(0, 8))
        self.build_pass = tk.StringVar(value="0" * 32)
        ttk.Entry(opt, textvariable=self.build_pass, width=30, show="•").pack(side="left")
        self.b_sdk_lbl = tk.Label(opt, font=self.F_H2, bd=0)
        self.b_sdk_lbl._is_field_label = True
        self.b_sdk_lbl.pack(side="left", padx=(14, 8))
        self.build_sdk = tk.StringVar(value="9.00")
        ttk.Entry(opt, textvariable=self.build_sdk, width=8).pack(side="left")
        self.b_mode_lbl = tk.Label(opt, font=self.F_H2, bd=0)
        self.b_mode_lbl._is_field_label = True
        self.b_mode_lbl.pack(side="left", padx=(14, 8))
        self.build_mode = tk.StringVar(value="Application")
        ttk.Combobox(opt, textvariable=self.build_mode, width=14, state="readonly",
                     values=["Application", "Homebrew"]).pack(side="left")
        self.b_btn_go = ttk.Button(card, style="Accent.TButton", command=self._do_build)
        self.b_btn_go.pack(anchor="w", pady=(14, 2))
        # log card
        logc = self._card(f)
        logc.pack(fill="both", expand=True)
        top = tk.Frame(logc, bd=0, highlightthickness=0)
        top.pack(fill="x", pady=(0, 8))
        self.b_log_lbl = tk.Label(top, font=self.F_H2, bd=0)
        self.b_log_lbl.pack(side="left")
        self.build_out_text = tk.Text(logc, height=10, wrap="word", font=self.F_MONO)
        self.build_out_text.pack(fill="both", expand=True)

    def _set_build_src(self, is_image):
        self.build_is_image.set(is_image)
        self._paint_build_seg()
        self._lang_build_src()

    def _paint_build_seg(self):
        c = self._colors()
        is_image = self.build_is_image.get()
        for btn, active in ((self.b_seg_folder, not is_image), (self.b_seg_exfat, is_image)):
            btn.configure(bg=ACCENT if active else c["side"],
                          fg="white" if active else c["muted"],
                          activebackground=ACCENT if active else c["nav_hover"])
        try:
            self.b_seg.configure(bg=c["side"], highlightbackground=c["border"])
        except Exception:
            pass

    def _lang_build(self):
        self.b_kicker.configure(text=self.t("build_kicker"))
        self.b_title.configure(text=self.t("build_title"))
        self.b_desc.configure(text=self.t("build_desc"))
        self.b_seg_folder.configure(text=self.t("src_folder"))
        self.b_seg_exfat.configure(text=self.t("src_exfat"))
        self._lang_build_src()
        labels = {"out_label": self.t("out_label"), "tmp_label": self.t("tmp_label")}
        for key, lbl, _e, btn in self.b_rows:
            lbl.configure(text=labels[key])
            btn.configure(text=self.t("btn_browse"))
        self.b_src_lbl.configure(text=self.t("source_label"))
        self.b_btn_browse.configure(text=self.t("btn_browse"))
        self.b_pass_lbl.configure(text=self.t("passcode_label"))
        self.b_sdk_lbl.configure(text=self.t("sdk_label"))
        self.b_mode_lbl.configure(text=self.t("mode_label"))
        self.b_btn_go.configure(text=self.t("btn_build"))
        self.b_log_lbl.configure(text=self.t("log_label"))
        self._paint_build_seg()
        self.apply_mode()

    def _lang_build_src(self):
        is_image = self.build_is_image.get()
        self.b_entry._placeholder = self.t("build_src_ph")
        if not self.build_src.get():
            try:
                self.b_entry.delete(0, "end")
                self.b_entry.insert(0, self.t("build_src_ph"))
            except Exception:
                pass

    def _browse_build_src(self):
        if self.build_is_image.get():
            p = filedialog.askopenfilename(title=self.t("src_exfat"),
                                           filetypes=[("exFAT", "*.exfat"), ("All", "*.*")])
        else:
            p = filedialog.askdirectory(title=self.t("src_folder"))
        if p:
            self.build_src.set(p)

    def _browse_build_out(self, key):
        if key == "out_label":
            p = filedialog.asksaveasfilename(title=self.t("out_label"),
                                             defaultextension=".pkg",
                                             filetypes=[("PKG", "*.pkg")])
            if p:
                self.build_out.set(p if p.endswith(".pkg") else p + ".pkg")
        else:
            d = filedialog.askdirectory(title=self.t("tmp_label"))
            if d:
                self.build_tmp.set(d)

    def _do_build(self):
        if self.busy:
            return
        src = self._get_entry_value(self.b_entry, self.build_src)
        out = self.build_out.get().strip()
        tmp = self.build_tmp.get().strip()
        if not src or not out or not tmp:
            messagebox.showwarning(self.t("warn_title"), self.t("ask_fill"))
            return
        cmd = "build-exfat" if self.build_is_image.get() else "build-folder"
        args = [cmd, src, out, tmp, self.build_pass.get().strip() or "0" * 32,
                "--sdk", self.build_sdk.get().strip() or "9.00",
                "--mode", self.build_mode.get().strip() or "Application"]
        self.build_out_text.delete("1.0", "end")

        def on_done_build(rc, transcript):
            self._paint_card_widget(self.build_out_text, self._colors())
            if rc == 0:
                messagebox.showinfo(self.t("info_title"), self.t("build_done", path=out))
                self.status_var.set(self.t("done"))
            else:
                messagebox.showerror(self.t("err_title"), transcript[-3000:])
                self.status_var.set(self.t("failed"))

        self.run_task(args, log_widget=self.build_out_text, status_msg=self.t("working"),
                      on_done=on_done_build, timeout=None)

    # ============ EXTRACTOR
    def _build_extract(self):
        f = self.pages["extractor"]
        self.e_kicker, self.e_title, self.e_desc = self._page_header(f)
        card = self._card(f)
        self.ext_pkg = tk.StringVar()
        self.ext_dest = tk.StringVar()
        self.ext_tmp = tk.StringVar(value=os.path.join(os.path.expanduser("~"), "Downloads", "fpkg-tmp"))
        self.ext_pass = tk.StringVar(value="0" * 32)
        self.e_rows = []
        for key in ("ext_pkg", "ext_dest", "ext_tmp"):
            lbl = tk.Label(card, font=self.F_H2, bd=0)
            lbl._is_field_label = True
            lbl.pack(anchor="w", pady=(8 if key != "ext_pkg" else 0, 6))
            row = tk.Frame(card, bd=0, highlightthickness=0)
            row.pack(fill="x")
            var = getattr(self, key)
            e = ttk.Entry(row, textvariable=var, width=52)
            e.pack(side="left", fill="x", expand=True, padx=(0, 8))
            btn = ttk.Button(row, width=10, command=lambda k=key: self._browse_ext(k))
            btn.pack(side="left")
            self.e_rows.append((key, lbl, e, btn))
        plbl = tk.Label(card, font=self.F_H2, bd=0)
        plbl._is_field_label = True
        plbl.pack(anchor="w", pady=(12, 6))
        self.e_pass_lbl = plbl
        ttk.Entry(card, textvariable=self.ext_pass, width=36, show="•").pack(anchor="w")
        self.e_btn_go = ttk.Button(card, style="Accent.TButton", command=self._do_extractor)
        self.e_btn_go.pack(anchor="w", pady=(14, 2))
        log = self._card(f)
        log.pack(fill="both", expand=True)
        top = tk.Frame(log, bd=0, highlightthickness=0)
        top.pack(fill="x", pady=(0, 8))
        self.e_log_lbl = tk.Label(top, font=self.F_H2, bd=0)
        self.e_log_lbl.pack(side="left")
        self.ext_out = tk.Text(log, height=11, wrap="word", font=self.F_MONO)
        self.ext_out.pack(fill="both", expand=True)

    def _lang_extract(self):
        self.e_kicker.configure(text=self.t("ext_kicker"))
        self.e_title.configure(text=self.t("ext_title"))
        self.e_desc.configure(text=self.t("ext_desc"))
        labels = {"ext_pkg": self.t("ext_pkg"), "ext_dest": self.t("ext_dest"), "ext_tmp": self.t("ext_tmp")}
        for key, lbl, _e, btn in self.e_rows:
            lbl.configure(text=labels[key])
            btn.configure(text=self.t("btn_browse"))
        self.e_pass_lbl.configure(text=self.t("passcode_label"))
        self.e_btn_go.configure(text=self.t("btn_extract"))
        self.e_log_lbl.configure(text=self.t("log_label"))
        self.apply_mode()

    def _browse_ext(self, key):
        if key == "ext_tmp":
            d = filedialog.askdirectory(title=self.t("ext_tmp"))
            if d:
                getattr(self, key).set(d)
        elif key == "ext_dest":
            d = filedialog.askdirectory(title=self.t("choose_parent"))
            if d:
                import tkinter.simpledialog as sd
                n = sd.askstring(self.t("btn_extract"), self.t("new_folder_name"),
                                 initialvalue="pkg-extracted")
                if n:
                    getattr(self, key).set(os.path.join(d, n))
        else:
            p = filedialog.askopenfilename(title=self.t("ext_pkg"), filetypes=[("PKG", "*.pkg")])
            if p:
                getattr(self, key).set(p)

    def _do_extractor(self):
        if self.busy:
            return
        pkg, dest, tmp = self.ext_pkg.get().strip(), self.ext_dest.get().strip(), self.ext_tmp.get().strip()
        if not pkg or not dest or not tmp:
            messagebox.showwarning(self.t("warn_title"), self.t("ask_fill"))
            return
        self.ext_out.delete("1.0", "end")

        def on_done_ext(rc, transcript):
            self._paint_card_widget(self.ext_out, self._colors())
            self.status_var.set(self.t("done") if rc == 0 else self.t("failed"))
            if rc != 0:
                messagebox.showerror(self.t("err_title"), transcript[-3000:])

        self.run_task(["extract-pkg-folder", pkg, dest, tmp,
                       self.ext_pass.get().strip() or "0" * 32],
                      log_widget=self.ext_out, status_msg=self.t("working"),
                      on_done=on_done_ext, timeout=None)

    # ============ TROPHY
    def _build_trophy(self):
        f = self.pages["trophy"]
        self.tr_kicker, self.tr_title, self.tr_desc = self._page_header(f)
        card = self._card(f)
        row = tk.Frame(card, bd=0, highlightthickness=0)
        row.pack(fill="x")
        self.tr_ip_lbl = tk.Label(row, font=self.F_H2, bd=0)
        self.tr_ip_lbl._is_field_label = True
        self.tr_ip_lbl.pack(side="left", padx=(0, 8))
        self.tr_host = tk.StringVar()
        ttk.Entry(row, textvariable=self.tr_host, width=18).pack(side="left")
        self.tr_btn_detect = ttk.Button(row, command=self._tr_detect)
        self.tr_btn_detect.pack(side="left", padx=(8, 0))
        self.tr_ppsa_lbl = tk.Label(row, font=self.F_H2, bd=0)
        self.tr_ppsa_lbl._is_field_label = True
        self.tr_ppsa_lbl.pack(side="left", padx=(16, 8))
        self.tr_ppsa = tk.StringVar()
        ttk.Entry(row, textvariable=self.tr_ppsa, width=14).pack(side="left")
        self.tr_btn_load = ttk.Button(row, style="Accent.TButton", command=self._tr_list)
        self.tr_btn_load.pack(side="left", padx=(8, 0))
        lst = self._card(f)
        lst.pack(fill="both", expand=True)
        self.tr_tree = ttk.Treeview(lst, columns=("id", "state", "grade", "name"),
                                    show="headings", height=12)
        self.tr_tree.pack(side="left", fill="both", expand=True)
        vsb = ttk.Scrollbar(lst, orient="vertical", command=self.tr_tree.yview)
        vsb.pack(side="right", fill="y")
        self.tr_tree.configure(yscrollcommand=vsb.set)
        foot = ttk.Frame(f)
        foot.pack(fill="x", pady=(2, 0))
        self.tr_btn_one = ttk.Button(foot, command=self._tr_unlock)
        self.tr_btn_one.pack(side="left", padx=(0, 6))
        self.tr_btn_all = ttk.Button(foot, command=self._tr_unlock_all)
        self.tr_btn_all.pack(side="left", padx=(0, 6))
        self.tr_btn_refresh = ttk.Button(foot, command=self._tr_list)
        self.tr_btn_refresh.pack(side="left")
        self.tr_status = tk.StringVar(value="")
        ttk.Label(foot, textvariable=self.tr_status, font=self.F_SMALL).pack(side="right")

    def _lang_trophy(self):
        self.tr_kicker.configure(text=self.t("tr_kicker"))
        self.tr_title.configure(text=self.t("tr_title"))
        self.tr_desc.configure(text=self.t("tr_desc"))
        self.tr_ip_lbl.configure(text=self.t("tr_ip"))
        self.tr_btn_detect.configure(text=self.t("btn_detect"))
        self.tr_ppsa_lbl.configure(text=self.t("tr_ppsa"))
        self.tr_btn_load.configure(text=self.t("btn_load"))
        self.tr_tree.heading("id", text=self.t("col_id"))
        self.tr_tree.heading("state", text=self.t("col_status"))
        self.tr_tree.heading("grade", text=self.t("col_grade"))
        self.tr_tree.heading("name", text=self.t("col_name"))
        self.tr_tree.column("id", width=70)
        self.tr_tree.column("state", width=120)
        self.tr_tree.column("grade", width=130)
        self.tr_tree.column("name", width=440)
        self.tr_btn_one.configure(text=self.t("btn_unlock_sel"))
        self.tr_btn_all.configure(text=self.t("btn_unlock_all"))
        self.tr_btn_refresh.configure(text=self.t("btn_refresh"))
        self.apply_mode()

    def _tr_detect(self):
        if self.busy:
            return
        host = self.tr_host.get().strip()
        if not host:
            messagebox.showwarning(self.t("warn_title"), self.t("ask_ip"))
            return
        self.set_busy(True, self.t("detecting"))
        def work():
            rc, out, err = run_backend(["trophy-detect", host], timeout=60)
            def done():
                self.set_busy(False)
                if rc == 0:
                    for line in out.splitlines():
                        if line.startswith("PPSA="):
                            self.tr_ppsa.set(line.split("=", 1)[1].strip())
                    self._tr_list()
                else:
                    messagebox.showerror(self.t("err_title"), (err or out)[:2000])
                    self.status_var.set(self.t("failed"))
            self.after(0, done)
        threading.Thread(target=work, daemon=True).start()

    def _tr_list(self):
        if self.busy:
            return
        host, ppsa = self.tr_host.get().strip(), self.tr_ppsa.get().strip().upper()
        if not host or not ppsa:
            messagebox.showwarning(self.t("warn_title"), self.t("ask_ip_ppsa"))
            return
        self.set_busy(True, self.t("loading_trophies"))
        def work():
            rc, out, err = run_backend(["trophy-list", host, ppsa], timeout=120)
            def done():
                self.set_busy(False)
                for i in self.tr_tree.get_children():
                    self.tr_tree.delete(i)
                if rc != 0:
                    messagebox.showerror(self.t("err_title"), (err or out)[:3000])
                    return
                n = 0
                for line in out.splitlines():
                    if line.startswith("PPSA="):
                        self.tr_status.set(line.strip())
                        continue
                    p = line.split("\t")
                    if len(p) >= 4 and p[0].strip().isdigit():
                        tag = "even" if n % 2 == 0 else "odd"
                        self.tr_tree.insert("", "end", values=(p[0], p[1], p[2], p[3]), tags=(tag,))
                        n += 1
                try:
                    c = self._colors()
                    self.tr_tree.tag_configure("even", background="")
                    self.tr_tree.tag_configure("odd", background=c["zebra"])
                except Exception:
                    pass
                self.tr_status.set(self.t("trophy_loaded", n=n))
            self.after(0, done)
        threading.Thread(target=work, daemon=True).start()

    def _tr_unlock(self):
        if self.busy:
            return
        sel = self.tr_tree.selection()
        if not sel:
            messagebox.showinfo(self.t("info_title"), self.t("ask_trophy"))
            return
        tid = self.tr_tree.item(sel[0], "values")[0]
        host, ppsa = self.tr_host.get().strip(), self.tr_ppsa.get().strip().upper()
        self.set_busy(True, self.t("unlocking", tid=tid))
        def work():
            rc, out, err = run_backend(["trophy-unlock", host, ppsa, str(tid)], timeout=180)
            def done():
                self.set_busy(False)
                if rc != 0:
                    messagebox.showerror(self.t("err_title"), (err or out)[:3000])
                self._tr_list()
            self.after(0, done)
        threading.Thread(target=work, daemon=True).start()

    def _tr_unlock_all(self):
        if self.busy:
            return
        if not messagebox.askyesno(self.t("btn_unlock_all"), self.t("confirm_all")):
            return
        host, ppsa = self.tr_host.get().strip(), self.tr_ppsa.get().strip().upper()
        self.set_busy(True, self.t("unlock_all"))
        def work():
            rc, out, err = run_backend(["trophy-unlock-all", host, ppsa], timeout=600)
            def done():
                self.set_busy(False)
                if rc != 0:
                    messagebox.showerror(self.t("err_title"), (err or out)[:3000])
                self._tr_list()
            self.after(0, done)
        threading.Thread(target=work, daemon=True).start()

    # ============ ABOUT
    def _build_about(self):
        f = self.pages["about"]
        self.a_kicker, self.a_title, self.a_desc = self._page_header(f)
        row = ttk.Frame(f)
        row.pack(fill="x", pady=(0, 14))
        self.a_mac = self._mini_card(row, ACCENT)
        self.a_win = self._mini_card(row, "#FF9F0A")
        self.a_mac.pack(side="left", fill="x", expand=True, padx=(0, 7))
        self.a_win.pack(side="left", fill="x", expand=True, padx=(7, 0))
        self.a_mac_t = tk.Label(self.a_mac, font=self.F_H2, bd=0)
        self.a_mac_t.pack(anchor="w")
        self.a_mac_b = tk.Label(self.a_mac, font=self.F_SMALL, bd=0, wraplength=360, justify="left")
        self.a_mac_b.pack(anchor="w", pady=(4, 0))
        self.a_win_t = tk.Label(self.a_win, font=self.F_H2, bd=0)
        self.a_win_t.pack(anchor="w")
        self.a_win_b = tk.Label(self.a_win, font=self.F_SMALL, bd=0, wraplength=360, justify="left")
        self.a_win_b.pack(anchor="w", pady=(4, 0))
        card = self._card(f)
        card.pack(fill="both", expand=True)
        self.a_text = tk.Text(card, wrap="word", font=self.F_BODY, height=14, bd=0,
                              highlightthickness=0, relief="flat")
        self.a_text.pack(fill="both", expand=True)

    def _mini_card(self, parent, stripe):
        outer = tk.Frame(parent, bd=0, highlightthickness=1)
        outer._is_card = True
        top = tk.Frame(outer, bg=stripe, height=3, bd=0, highlightthickness=0)
        top.pack(fill="x")
        inner = tk.Frame(outer, bd=0, highlightthickness=0)
        inner._is_card = True
        inner.pack(fill="x", padx=16, pady=12)
        return inner

    def _lang_about(self):
        self.a_kicker.configure(text=self.t("about_kicker"))
        self.a_title.configure(text=self.t("about_title"))
        try:
            self.a_desc.pack_forget()
        except Exception:
            pass
        self.a_mac_t.configure(text=self.t("mac_only_title"))
        self.a_mac_b.configure(text=self.t("mac_only_body"))
        self.a_win_t.configure(text=self.t("win_only_title"))
        self.a_win_b.configure(text=self.t("win_only_body"))
        self.a_text.configure(state="normal")
        self.a_text.delete("1.0", "end")
        self.a_text.insert("end", ABOUT_PT if self.lang == "pt-BR" else ABOUT_EN)
        self.a_text.configure(state="disabled")
        self.apply_mode()


def main():
    app = App()
    app.mainloop()

if __name__ == "__main__":
    main()
