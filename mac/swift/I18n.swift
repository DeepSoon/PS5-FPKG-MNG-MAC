import Foundation

// PS5 FPKG Builder — macOS native UI v4 (SwiftUI). Made by Leite.
let MacPortVersion = "1.0.0-NG"
let MadeByLeite = "Made by Leite"

enum Lang: String, CaseIterable {
    case ptBR = "pt-BR"
    case en = "en"
}

private let S: [String: [String: String]] = [
    "version_tag": ["pt-BR": "porte macOS · r30", "en": "macOS port · r30"],
    "made_by": ["pt-BR": "Feito por Leite", "en": "Made by Leite"],
    "backend_checking": ["pt-BR": "verificando backend…", "en": "checking backend…"],
    "backend_ok": ["pt-BR": "Backend pronto", "en": "Backend ready"],
    "backend_fail": ["pt-BR": "Backend falhou", "en": "Backend failed"],
    "ready": ["pt-BR": "Pronto.", "en": "Ready."],
    "nav_reader": ["pt-BR": "Leitor", "en": "Reader"],
    "nav_files": ["pt-BR": "Arquivos do PKG", "en": "PKG Files"],
    "nav_build": ["pt-BR": "Criar PKG", "en": "Build PKG"],
    "nav_ampr": ["pt-BR": "AMPR (LZ4)", "en": "AMPR (LZ4)"],
    "nav_extractor": ["pt-BR": "Extrator", "en": "Extractor"],
    "nav_trophy": ["pt-BR": "Troféus", "en": "Trophies"],
    "nav_about": ["pt-BR": "Sobre", "en": "About"],
    "to_light": ["pt-BR": "Claro", "en": "Light"],
    "to_dark": ["pt-BR": "Escuro", "en": "Dark"],
    "reader_kicker": ["pt-BR": "INSPECIONAR", "en": "INSPECT"],
    "reader_title": ["pt-BR": "Leitor de pacote", "en": "Package reader"],
    "reader_desc": ["pt-BR": "Inspecione uma pasta preparada (com sce_sys/param.json) ou um PKG debug legível. Metadados cifrados pedem o passcode de 32 caracteres.", "en": "Inspect a prepared folder (with sce_sys/param.json) or a readable debug PKG. Encrypted metadata needs the 32-character passcode."],
    "source_label": ["pt-BR": "Fonte", "en": "Source"],
    "source_ph": ["pt-BR": "Pasta de jogo ou arquivo .pkg…", "en": "Game folder or .pkg file…"],
    "btn_folder": ["pt-BR": "Pasta", "en": "Folder"],
    "btn_pkg": ["pt-BR": "Arquivo PKG", "en": "PKG file"],
    "passcode_label": ["pt-BR": "Passcode", "en": "Passcode"],
    "btn_inspect": ["pt-BR": "Inspecionar", "en": "Inspect"],
    "btn_clear": ["pt-BR": "Limpar", "en": "Clear"],
    "btn_copy": ["pt-BR": "Copiar", "en": "Copy"],
    "result_label": ["pt-BR": "Metadados", "en": "Metadata"],
    "result_empty": ["pt-BR": "Nada inspecionado ainda. Escolha uma fonte e clique em Inspecionar.", "en": "Nothing inspected yet. Choose a source and press Inspect."],
    "files_kicker": ["pt-BR": "NAVEGAR", "en": "BROWSE"],
    "files_title": ["pt-BR": "Navegador de arquivos do PKG", "en": "PKG file browser"],
    "files_desc": ["pt-BR": "Liste os arquivos internos de um pacote debug FIH/PPR sem extrair tudo, depois extraia um arquivo ou o conteúdo inteiro.", "en": "List the inner files of a debug FIH/PPR package without extracting everything, then extract a single file or the whole content."],
    "pkg_label": ["pt-BR": "Pacote", "en": "Package"],
    "pkg_ph": ["pt-BR": "Arquivo .pkg…", "en": ".pkg file…"],
    "btn_choose": ["pt-BR": "Escolher", "en": "Choose"],
    "btn_list": ["pt-BR": "Listar arquivos", "en": "List files"],
    "filter_ph": ["pt-BR": "Filtrar arquivos…", "en": "Filter files…"],
    "col_path": ["pt-BR": "Caminho", "en": "Path"],
    "col_size": ["pt-BR": "Tamanho", "en": "Size"],
    "col_state": ["pt-BR": "Estado", "en": "State"],
    "btn_extract_sel": ["pt-BR": "Extrair selecionado", "en": "Extract selected"],
    "btn_extract_all": ["pt-BR": "Extrair tudo", "en": "Extract all"],
    "count_files": ["pt-BR": "{shown} de {total} arquivos", "en": "{shown} of {total} files"],
    "build_kicker": ["pt-BR": "CRIAR", "en": "BUILD"],
    "build_title": ["pt-BR": "Criar um PKG debug", "en": "Build a debug PKG"],
    "build_desc": ["pt-BR": "De uma pasta preparada ou de um dump exFAT cru. O exFAT passa por uma cópia temporária porque o macOS não monta dumps crus. A saída é um pacote FullDebug FIH (Kraken BuiltIn).", "en": "From a prepared folder or a raw exFAT dump. exFAT is staged through a temporary copy because macOS cannot mount raw dumps. Output is a FullDebug FIH package (BuiltIn Kraken)."],
    "src_folder": ["pt-BR": "Pasta", "en": "Folder"],
    "src_exfat": ["pt-BR": "Imagem exFAT", "en": "exFAT image"],
    "out_label": ["pt-BR": "PKG de saída", "en": "Output PKG"],
    "tmp_label": ["pt-BR": "Temporária", "en": "Temporary"],
    "sdk_label": ["pt-BR": "SDK", "en": "SDK"],
    "mode_label": ["pt-BR": "Modo", "en": "Mode"],
    "btn_build": ["pt-BR": "Criar PKG", "en": "Build PKG"],
    "build_done": ["pt-BR": "PKG criado:\n{path}", "en": "PKG created:\n{path}"],
    "ampr_kicker": ["pt-BR": "COMPRIMIR", "en": "COMPRESS"],
    "ampr_title": ["pt-BR": "Pacotes AMPR (LZ4)", "en": "AMPR (LZ4) packs"],
    "ampr_desc": ["pt-BR": "Comprima uma pasta de jogo ou um dump exFAT em pacotes LZ4 seekable (nível 0–12), ou extraia um pack de volta para pasta. Executáveis e mídia já comprimida ficam de fora automaticamente.", "en": "Compress a game folder or a raw exFAT dump into seekable LZ4 packs (level 0–12), or extract a pack back to a folder. Executables and already-compressed media stay out automatically."],
    "level_label": ["pt-BR": "Nível", "en": "Level"],
    "level_fast": ["pt-BR": "rápido", "en": "fast"],
    "level_hc": ["pt-BR": "compressão alta", "en": "high compression"],
    "btn_compress": ["pt-BR": "Comprimir em LZ4", "en": "Compress to LZ4"],
    "btn_unpack": ["pt-BR": "Extrair pack", "en": "Extract pack"],
    "ampr_packdir": ["pt-BR": "Pasta do pack (com ampr_assets.index)", "en": "Pack folder (with ampr_assets.index)"],
    "ampr_dest": ["pt-BR": "Pasta de destino", "en": "Destination folder"],
    "ampr_done": ["pt-BR": "Pack criado:\n{path}", "en": "Pack created:\n{path}"],
    "ampr_undone": ["pt-BR": "Extraído para:\n{path}", "en": "Extracted to:\n{path}"],
    "ext_kicker": ["pt-BR": "EXPORTAR", "en": "EXPORT"],
    "ext_title": ["pt-BR": "PKG para pasta", "en": "PKG to folder"],
    "ext_desc": ["pt-BR": "Exporte o conteúdo legível do pacote para uma pasta normal, sem rebuild. O PKG original é aberto somente-leitura e nunca é modificado.", "en": "Export readable package content to a normal folder, without rebuilding. The original PKG is opened read-only and never modified."],
    "ext_pkg": ["pt-BR": "Arquivo PKG", "en": "PKG file"],
    "ext_dest": ["pt-BR": "Destino", "en": "Destination"],
    "ext_tmp": ["pt-BR": "Pasta temporária", "en": "Temporary folder"],
    "btn_browse": ["pt-BR": "Procurar", "en": "Browse"],
    "btn_extract": ["pt-BR": "Extrair para a pasta", "en": "Extract to folder"],
    "log_label": ["pt-BR": "Registro", "en": "Log"],
    "tr_kicker": ["pt-BR": "CONSOLE", "en": "CONSOLE"],
    "tr_title": ["pt-BR": "Desbloqueador de troféus", "en": "Trophy unlocker"],
    "tr_desc": ["pt-BR": "Funciona via FTP (porta 2121) com o payload loader na porta 9021. Sem ELF externo — o payload já vem embutido.", "en": "Works over FTP (port 2121) with the payload loader on port 9021. No external ELF needed — the payload is embedded."],
    "tr_ip": ["pt-BR": "IP do PS5", "en": "PS5 IP address"],
    "btn_detect": ["pt-BR": "Detectar jogo", "en": "Detect game"],
    "tr_ppsa": ["pt-BR": "Title ID", "en": "Title ID"],
    "btn_load": ["pt-BR": "Carregar troféus", "en": "Load trophies"],
    "col_id": ["pt-BR": "ID", "en": "ID"],
    "col_status": ["pt-BR": "Estado", "en": "Status"],
    "col_grade": ["pt-BR": "Nível", "en": "Grade"],
    "col_name": ["pt-BR": "Troféu", "en": "Trophy"],
    "btn_unlock_sel": ["pt-BR": "Desbloquear selecionado", "en": "Unlock selected"],
    "btn_unlock_all": ["pt-BR": "Desbloquear todos", "en": "Unlock all"],
    "btn_refresh": ["pt-BR": "Atualizar", "en": "Refresh"],
    "about_kicker": ["pt-BR": "INFORMAÇÃO", "en": "INFO"],
    "about_title": ["pt-BR": "Sobre este porte para Mac", "en": "About this Mac port"],
    "working": ["pt-BR": "Trabalhando…", "en": "Working…"],
    "done": ["pt-BR": "Concluído.", "en": "Done."],
    "failed": ["pt-BR": "Falhou.", "en": "Failed."],
    "transferred": ["pt-BR": "{pct}% · {done} de {total} MB · {speed} MB/s · faltam {left} MB{eta}", "en": "{pct}% · {done} of {total} MB · {speed} MB/s · {left} MB left{eta}"],
    "warn_title": ["pt-BR": "Atenção", "en": "Attention"],
    "err_title": ["pt-BR": "Erro", "en": "Error"],
    "ask_pkg": ["pt-BR": "Escolha primeiro um arquivo .pkg válido.", "en": "Choose a valid .pkg file first."],
    "ask_src": ["pt-BR": "Escolha primeiro uma pasta de jogo ou um arquivo .pkg.", "en": "Choose a game folder or a .pkg file first."],
    "ask_select": ["pt-BR": "Selecione primeiro um arquivo na lista.", "en": "Select a file in the list first."],
    "ask_fill": ["pt-BR": "Preencha o PKG, o destino e a pasta temporária.", "en": "Fill in the PKG, destination and temporary folder."],
    "ask_ampr_src": ["pt-BR": "Escolha a pasta do jogo ou a imagem .exfat e a pasta de saída.", "en": "Choose the game folder or .exfat image and the output folder."],
    "ask_ampr_unpack": ["pt-BR": "Escolha a pasta do pack e o destino.", "en": "Choose the pack folder and the destination."],
    "ask_ip": ["pt-BR": "Digite o IP do PS5.", "en": "Enter the PS5 IP address."],
    "ask_ip_ppsa": ["pt-BR": "O IP e o Title ID são obrigatórios.", "en": "The IP address and title ID are required."],
    "ask_trophy": ["pt-BR": "Selecione primeiro um troféu.", "en": "Select a trophy first."],
    "confirm_all": ["pt-BR": "Desbloquear TODOS os troféus listados?", "en": "Unlock ALL listed trophies?"],
    "extract_ok": ["pt-BR": "Conteúdo extraído para:\n{path}", "en": "Content extracted to:\n{path}"],
    "extracted_to": ["pt-BR": "Extraído para:\n{path}", "en": "Extracted to:\n{path}"],
    "save_as": ["pt-BR": "Salvar “{name}” como…", "en": "Save “{name}” as…"],
    "mac_only_title": ["pt-BR": "Funciona no Mac", "en": "Runs on Mac"],
    "mac_only_body": ["pt-BR": "Leitor · arquivos · extração · PKG-para-pasta · criar PKG (pasta/exFAT) · packs AMPR (LZ4) · troféus", "en": "Reader · files · extraction · PKG-to-folder · build PKG (folder/exFAT) · AMPR (LZ4) packs · trophies"],
    "win_only_title": ["pt-BR": "Ainda exige Windows", "en": "Still Windows-only"],
    "win_only_body": ["pt-BR": "Updates delta/backport (SDK Sony) · exFAT zero-copy · FFPKG/UFS2", "en": "Delta/backport updates (Sony SDK) · zero-copy exFAT · FFPKG/UFS2"],
]

func tr(_ key: String, _ lang: Lang, _ args: [String: String] = [:]) -> String {
    var s = S[key]?[lang.rawValue] ?? S[key]?["en"] ?? key
    for (k, v) in args { s = s.replacingOccurrences(of: "{\(k)}", with: v) }
    return s
}

let AboutEN = """
The original tool is a WinForms + .NET 9 Windows application that depends on OSFMount, prospero-pub-cmd.exe from the Sony SDK and libScePubTools x64 + UFS2Tool/Dokan.

This Mac port (native SwiftUI interface, Made by Leite) reuses the portable LibProsperoPkg core. Reader, extraction, trophy unlocking, debug PKG building (folder / raw exFAT, managed BuiltIn Kraken) and AMPR (LZ4, levels 0–12) packs all run natively on macOS.

Still Windows-only: delta/backport updates (Sony SDK), zero-copy exFAT mount, FFPKG/UFS2 tools.

AMPR: reference format/tools by drakmor/ampr_emu (GPLv3); general flow inspired by Nazky's Lazy_AMPR. Original credits: Zer0Day · Drakmor / ShadowMountPlus · svenGDK (LibProsperoPKG, UFS2Tool) · EchoStretch / cragson (A53) · kerrdec97 · MkPFS / Lazy_MkPFS.
"""

let AboutPT = """
A ferramenta original é um aplicativo WinForms + .NET 9 para Windows que depende do OSFMount, do prospero-pub-cmd.exe do SDK da Sony e do libScePubTools x64 + UFS2Tool/Dokan.

Este porte para Mac (interface nativa SwiftUI, feito por Leite) reaproveita o núcleo portável LibProsperoPkg. Leitor, extração, troféus, criação de PKG debug (pasta / exFAT cru, Kraken BuiltIn gerenciado) e pacotes AMPR (LZ4, níveis 0–12) rodam nativamente no macOS.

Ainda exige Windows: updates delta/backport (SDK Sony), mount exFAT zero-copy, ferramentas FFPKG/UFS2.

AMPR: formato e ferramentas de referência drakmor/ampr_emu (GPLv3); fluxo geral inspirado no Lazy_AMPR de Nazky. Créditos originais: Zer0Day · Drakmor / ShadowMountPlus · svenGDK (LibProsperoPKG, UFS2Tool) · EchoStretch / cragson (A53) · kerrdec97 · MkPFS / Lazy_MkPFS.
"""

func fmtMB(_ bytes: Double, lang: Lang) -> String {
    let v = bytes / 1048576.0
    let s = String(format: "%.1f", v)
    return lang == .ptBR ? s.replacingOccurrences(of: ".", with: ",") : s
}

func fmtETA(_ seconds: Double, lang: Lang) -> String {
    if seconds.isNaN || seconds.isInfinite || seconds < 0 { return "" }
    if seconds < 90 { return lang == .ptBR ? " · ~\(Int(seconds)) s" : " · ~\(Int(seconds))s" }
    let m = Int((seconds / 60).rounded())
    return lang == .ptBR ? " · ~\(m) min" : " · ~\(m) min"
}
