from pathlib import Path
import re, json, hashlib
import cairosvg
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
IMG = ROOT / 'Resources' / 'Images'
OUT_SVG = ROOT / 'Design' / 'ProfessionalIcons'
OUT_SVG.mkdir(parents=True, exist_ok=True)

# Intentional, consistent monoline icon artwork (Lucide-inspired design language,
# drawn specifically for Himo; no third-party artwork is copied).
SHAPES = {
 'home': '<path d="m3 10.8 9-7.5 9 7.5"/><path d="M5.5 9.2V21h5.1v-6h2.8v6h5.1V9.2"/>',
 'user': '<circle cx="12" cy="7.5" r="3.3"/><path d="M5 21v-1.4a5.1 5.1 0 0 1 5.1-5.1h3.8a5.1 5.1 0 0 1 5.1 5.1V21"/>',
 'users': '<path d="M16.5 20.5v-1.2a4.2 4.2 0 0 0-4.2-4.2h-4A4.2 4.2 0 0 0 4.1 19.3v1.2"/><circle cx="10.3" cy="7.2" r="3.2"/><path d="M16.2 4.3a3.2 3.2 0 0 1 0 6.2M19.9 20.5v-1.2a4.2 4.2 0 0 0-2.9-4"/>',
 'search': '<circle cx="10.8" cy="10.8" r="6.8"/><path d="m16 16 5 5"/>',
 'settings': '<path d="M12 8.3a3.7 3.7 0 1 0 0 7.4 3.7 3.7 0 0 0 0-7.4Z"/><path d="m19.1 13.6 1.1.8-1.5 2.5-1.3-.5a7.3 7.3 0 0 1-1.8 1l-.2 1.4h-2.9l-.2-1.4a7.3 7.3 0 0 1-1.8-1l-1.3.5-1.5-2.5 1.1-.8a6.7 6.7 0 0 1 0-2.2l-1.1-.8 1.5-2.5 1.3.5a7.3 7.3 0 0 1 1.8-1l.2-1.4h2.9l.2 1.4a7.3 7.3 0 0 1 1.8 1l1.3-.5 1.5 2.5-1.1.8a6.7 6.7 0 0 1 0 2.2Z"/>',
 'phone': '<path d="M7.1 3.5 9 6.9a1.7 1.7 0 0 1-.2 1.9L7.6 10a15.1 15.1 0 0 0 6.4 6.4l1.2-1.2a1.7 1.7 0 0 1 1.9-.2l3.4 1.9a1.7 1.7 0 0 1 .8 1.9l-.4 1.3a2 2 0 0 1-2 1.4A18 18 0 0 1 2.5 5.3a2 2 0 0 1 1.4-2l1.3-.4a1.7 1.7 0 0 1 1.9.6Z"/>',
 'phone_end': '<path d="M3.1 14.2a18.2 18.2 0 0 1 17.8 0l-2 3a1.4 1.4 0 0 1-2 .4l-2.2-1.4a1.5 1.5 0 0 1-.6-1.8l.2-.5a12.8 12.8 0 0 0-6.6 0l.2.5a1.5 1.5 0 0 1-.6 1.8l-2.2 1.4a1.4 1.4 0 0 1-2-.4l-2-3a1.8 1.8 0 0 1 .0-2.1Z"/>',
 'camera': '<rect x="3" y="6.7" width="18" height="13.2" rx="2.2"/><path d="m8 6.7 1.3-2h5.4l1.3 2"/><circle cx="12" cy="13.3" r="3.5"/>',
 'camera_off': '<rect x="3" y="6.7" width="18" height="13.2" rx="2.2"/><path d="m8 6.7 1.3-2h5.4l1.3 2M9.8 10.6a3.5 3.5 0 0 1 4.8 4.8"/><path d="M3 3 21 21"/>',
 'mic': '<rect x="9" y="2.5" width="6" height="12" rx="3"/><path d="M5.5 10.5v1.2a6.5 6.5 0 0 0 13 0v-1.2M12 18.2V22M8.5 22h7"/>',
 'mic_off': '<path d="M9 9V5.5a3 3 0 0 1 5.8-1.1M15 9.5v2.2a3 3 0 0 1-5.3 1.9"/><path d="M5.5 10.5v1.2a6.5 6.5 0 0 0 11 4.6M18.5 10.5v1.2c0 .8-.1 1.5-.4 2.2M12 18.2V22M8.5 22h7M3 3l18 18"/>',
 'video': '<rect x="3" y="6.5" width="12.5" height="11" rx="2"/><path d="m15.5 10 5-3v10l-5-3"/>',
 'chat': '<path d="M20.5 11.5a7.8 7.8 0 0 1-.9 3.7 8.2 8.2 0 0 1-7.3 4.3 7.8 7.8 0 0 1-3.7-.9L3 20l1.4-5.1a7.8 7.8 0 0 1-.9-3.7A8.2 8.2 0 0 1 7.8 4a7.8 7.8 0 0 1 3.7-.9h.5a8.2 8.2 0 0 1 8.5 8v.4Z"/><path d="M8 11.5h.1M12 11.5h.1M16 11.5h.1"/>',
 'chat_empty': '<path d="M20.5 11.5a7.8 7.8 0 0 1-.9 3.7 8.2 8.2 0 0 1-7.3 4.3 7.8 7.8 0 0 1-3.7-.9L3 20l1.4-5.1a7.8 7.8 0 0 1-.9-3.7A8.2 8.2 0 0 1 7.8 4a7.8 7.8 0 0 1 3.7-.9h.5a8.2 8.2 0 0 1 8.5 8v.4Z"/>',
 'mail': '<rect x="2.7" y="4.5" width="18.6" height="15" rx="2.2"/><path d="m3.5 6 8.5 6.6L20.5 6"/>',
 'lock': '<rect x="4.3" y="10" width="15.4" height="11" rx="2.2"/><path d="M7.5 10V7a4.5 4.5 0 0 1 9 0v3M12 14v3"/>',
 'shield': '<path d="M12 22s8-3.7 8-10.2V5.2L12 2 4 5.2v6.6C4 18.3 12 22 12 22Z"/><path d="m8.8 12.1 2.1 2.1 4.5-4.7"/>',
 'privacy': '<path d="M12 22s8-3.7 8-10.2V5.2L12 2 4 5.2v6.6C4 18.3 12 22 12 22Z"/><path d="M7.5 11.5s1.6-2.5 4.5-2.5 4.5 2.5 4.5 2.5-1.6 2.5-4.5 2.5-4.5-2.5-4.5-2.5Z"/><circle cx="12" cy="11.5" r="1"/>',
 'eye': '<path d="M2.3 12s3.4-6.1 9.7-6.1 9.7 6.1 9.7 6.1-3.4 6.1-9.7 6.1S2.3 12 2.3 12Z"/><circle cx="12" cy="12" r="2.7"/>',
 'eye_off': '<path d="m3 3 18 18M10.6 6.1a9 9 0 0 1 1.4-.1c6.3 0 9.7 6 9.7 6a16.5 16.5 0 0 1-3 3.6M6.2 6.4C3.7 8 2.3 12 2.3 12s3.4 6.1 9.7 6.1c1.3 0 2.5-.3 3.5-.7"/><path d="M9.9 9.9a3 3 0 0 0 4.2 4.2"/>',
 'bell': '<path d="M18 8.8a6 6 0 0 0-12 0c0 7-3 7-3 8.5h18c0-1.5-3-1.5-3-8.5ZM10 21h4"/>',
 'bell_off': '<path d="m3 3 18 18M9.3 5.2A6 6 0 0 1 18 9c0 3.2.6 4.8 1.3 5.8M6.2 6.5C5.5 7.5 5 8.5 5 9c0 7-3 7-3 8.5h16M10 21h4"/>',
 'palette': '<path d="M12 3a9 9 0 1 0 0 18h1.6a2.1 2.1 0 0 0 1.6-3.5 1.8 1.8 0 0 1 1.4-3h1.2A3.2 3.2 0 0 0 21 11.3 8.8 8.8 0 0 0 12 3Z"/><circle cx="7.3" cy="11" r="1"/><circle cx="10" cy="7.5" r="1"/><circle cx="15" cy="7.8" r="1"/>',
 'text': '<path d="M4 5.2h16M12 5.2V21M8.5 21h7M4 10.2h3M4 14.7h3M17 10.2h3M17 14.7h3"/>',
 'globe': '<circle cx="12" cy="12" r="9.2"/><path d="M2.8 12h18.4M12 2.8a15 15 0 0 1 0 18.4M12 2.8a15 15 0 0 0 0 18.4"/>',
 'network': '<circle cx="5" cy="12" r="2.2"/><circle cx="19" cy="5" r="2.2"/><circle cx="19" cy="19" r="2.2"/><path d="m7 11 9.7-4.8M7 13l9.7 4.8"/>',
 'storage': '<ellipse cx="12" cy="5.2" rx="8.2" ry="2.8"/><path d="M3.8 5.2v6.5c0 1.5 3.7 2.8 8.2 2.8s8.2-1.3 8.2-2.8V5.2M3.8 11.7v6.8c0 1.5 3.7 2.7 8.2 2.7s8.2-1.2 8.2-2.7v-6.8"/>',
 'archive': '<path d="M3 7.5h18v13H3zM2 3.5h20v4H2zM9 11.5h6"/>',
 'backup': '<path d="M7.5 18.5H6a4 4 0 0 1-.3-8 6.5 6.5 0 0 1 12.5-1.2 4.6 4.6 0 0 1-.7 9.2H16"/><path d="M12 21V12m-3.2 3.2L12 12l3.2 3.2"/>',
 'camera': '<rect x="3" y="6.5" width="18" height="13" rx="2"/><path d="m8 6.5 1.3-2h5.4l1.3 2"/><circle cx="12" cy="13" r="3.3"/>',
 'vibration': '<rect x="7.1" y="2.5" width="9.8" height="19" rx="2.1"/><path d="M3.8 7v10M20.2 7v10M1.5 9.5v5M22.5 9.5v5M10.5 18.3h3"/>',
 'sound': '<path d="M3 9v6h4l5 4V5L7 9H3Z"/><path d="M16 9.1a4.2 4.2 0 0 1 0 5.8M18.7 6.5a8 8 0 0 1 0 11"/>',
 'speaker_off': '<path d="M3 9v6h4l5 4V5L7 9H3Z"/><path d="m16 9 5 6m0-6-5 6"/>',
 'check': '<circle cx="12" cy="12" r="9.2"/><path d="m7.5 12.2 3 3.1 6.2-6.6"/>',
 'plus': '<path d="M12 5v14M5 12h14"/>',
 'trash': '<path d="M3.5 6.5h17M9 6.5V4h6v2.5M6 6.5l1 14h10l1-14M10 10v6.5M14 10v6.5"/>',
 'blocked': '<circle cx="12" cy="12" r="9.2"/><path d="m5.5 5.5 13 13"/>',
 'more': '<circle cx="5" cy="12" r=".8"/><circle cx="12" cy="12" r=".8"/><circle cx="19" cy="12" r=".8"/>',
 'back': '<path d="m14.5 5-7 7 7 7M7.5 12H21"/>',
 'close': '<path d="m6 6 12 12M18 6 6 18"/>',
 'send': '<path d="m22 2-7 20-4-9-9-4 20-7Z"/><path d="M22 2 11 13"/>',
 'attachment': '<path d="m8.5 12.5 6.9-6.9a3.4 3.4 0 0 1 4.8 4.8l-9.1 9.1a5.1 5.1 0 0 1-7.2-7.2l8.7-8.7"/>',
 'play': '<path d="M8 5.4a1 1 0 0 1 1.5-.9l10.2 6.6a1.1 1.1 0 0 1 0 1.8L9.5 19.5a1 1 0 0 1-1.5-.9V5.4Z" fill="{color}" stroke="{color}"/>',
 'pause': '<path d="M7.5 5.5h3v13h-3zM13.5 5.5h3v13h-3z" fill="{color}" stroke="{color}"/>',
 'stop': '<rect x="5.5" y="5.5" width="13" height="13" rx="2" fill="{color}" stroke="{color}"/>',
 'share': '<circle cx="18" cy="5" r="2.5"/><circle cx="6" cy="12" r="2.5"/><circle cx="18" cy="19" r="2.5"/><path d="m8.3 10.8 7.3-4.5M8.3 13.2l7.3 4.5"/>',
 'reply': '<path d="m9 8-5 4 5 4M4 12h9a7 7 0 0 1 7 7v1"/>',
 'edit': '<path d="m14 5 5 5M4 20l4.2-.9L19.3 8a2.1 2.1 0 0 0-3-3L5.2 16.1 4 20Z"/>',
 'login': '<path d="M10 4H5a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h5M14 16l4-4-4-4M18 12H8"/>',
 'image': '<rect x="3" y="3.5" width="18" height="17" rx="2"/><circle cx="8.5" cy="9" r="1.5"/><path d="m4 17 5-5 3.5 3.5 2.5-2.5 5 5"/>',
 'scroll_down': '<path d="M12 4v15M6 13l6 6 6-6"/>',
 'unarchive': '<path d="M3 7.5h18v13H3zM2 3.5h20v4H2zM9 12h6M12 15V9m-3 3 3-3 3 3"/>',
 'block': '<circle cx="12" cy="12" r="9.2"/><path d="M5.5 5.5 18.5 18.5"/><path d="M8 16V9h4.5a2 2 0 0 1 0 4H8"/>',
 'unblock': '<circle cx="12" cy="12" r="9.2"/><path d="m7.5 12 3 3 6-6"/>',
 'transfer': '<path d="M3 7h16l-3-3M21 17H5l3 3M19 7l-3 3M5 17l3-3"/>',
 'upload': '<path d="M12 16V4m-4 4 4-4 4 4M4 15v4a1.5 1.5 0 0 0 1.5 1.5h13A1.5 1.5 0 0 0 20 19v-4"/>',
 'download': '<path d="M12 4v12m-4-4 4 4 4-4M4 17v2.5A1.5 1.5 0 0 0 5.5 21h13a1.5 1.5 0 0 0 1.5-1.5V17"/>',
 'status': '<circle cx="12" cy="12" r="9"/><path d="M8.5 12.3 11 15l5-6"/>',
 'roaming': '<circle cx="12" cy="12" r="9"/><path d="M3.2 12h17.6M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"/><path d="m17.5 5.5 1.6 2M5 17l1.5 1.5"/>',
}

# Image name -> semantic glyph. All referenced legacy file names remain valid;
# only the artwork is replaced, which avoids breaking existing XAML handlers.
ALIASES = {
 'user':'user','profile':'user','contacts':'users','conversation':'chat','conversations':'chat','message':'chat','chat':'chat','chat_empty':'chat_empty',
 'home':'home','search':'search','settings':'settings','gear':'settings','phone':'phone','call':'phone','call_phone':'phone','phone_call':'phone',
 'call_end':'phone_end','end':'phone_end','camera':'camera','camera_off':'camera_off','mic':'mic','microphone':'mic','mic_off':'mic_off','video':'video',
 'mail':'mail','email':'mail','lock':'lock','shield':'shield','privacy':'privacy','eye':'eye','eye_open':'eye','open_eye':'eye','open':'eye','eye_closed':'eye_off','closed_eye':'eye_off','closed':'eye_off',
 'bell':'bell','bell_on':'bell','bell_off':'bell_off','palette':'palette','text':'text','globe':'globe','network':'network','storage':'storage','backup':'backup','archive':'archive',
 'media':'image','gallery':'image','image':'image','camera_image':'camera','vibration':'vibration','sound':'sound','volume':'sound','volume_off':'speaker_off','speaker':'sound','speaker_off':'speaker_off',
 'check':'check','plus':'plus','add':'plus','delete':'trash','trash':'trash','blocked':'blocked','block':'block','unblock':'unblock','more':'more','arrow_back':'back','back':'back','close':'close','cancel':'close',
 'white':'download','send':'send','attach':'attachment','attachment':'attachment','play':'play','play_white':'play','play_purple':'play','pause':'pause','pause_white':'pause','pause_purple':'pause','stop':'stop','share':'share','reply':'reply','edit':'edit','login':'login','scroll_down':'scroll_down',
 'unarchive':'unarchive','transfer':'transfer','upload':'upload','download':'download','status':'status','roaming':'roaming','camera_off_white':'camera_off','mic_off_white':'mic_off','mic_white':'mic','speaker_white':'sound','speaker_off_white':'speaker_off','phone_white':'phone','more_white':'more','end_white':'phone_end'
}

PALETTES = {
 'd_classic': ('#6C25D9','#3D176F','#DCCBFF'),
 'd_sky': ('#287AC4','#174B7B','#C8E6FF'),
 'd_light': ('#6B47B5','#43307C','#DDD1F9'),
 'd_graphite': ('#525D71','#323D50','#CDD5E1'),
 'd_mint': ('#1A927A','#125E50','#BDEDDD'),
 'd_rose': ('#C74780','#81264F','#FFD0E3'),
 'p_royal_gold': ('#F4C66B','#9E6E12','#FFE9AF'),
 'p_electric_blue': ('#59C8FF','#0677B8','#B4EBFF'),
 'p_neon_violet': ('#C76BFF','#8525CD','#E9BFFF'),
 'p_emerald': ('#5AE1B0','#118966','#B6FFE1'),
 'p_ruby': ('#FF7097','#BF2D58','#FFC4D4'),
 'p_silver': ('#D6DEEA','#8C9CB5','#F5F7FC'),
 'p_aurora': ('#48DFD4','#118F94','#B8FFFB'),
 'p_rose_gold': ('#EBA58E','#B96355','#FFE0D5'),
}
DEFAULT = ('#6C25D9','#3D176F','#DCCBFF')

# Build a canonical name from each asset's filename, including theme-prefixed files.
def semantic_from_filename(filename: str):
    stem = Path(filename).stem.lower()
    for style in PALETTES:
        prefix = f'himo_theme_{style}_'
        if stem.startswith(prefix):
            stem = stem[len(prefix):]
            break
    # Prefixes whose suffixes identify the icon.
    prefixes = [
        'settings_icon_', 'selection_', 'himo_icon_', 'himo_ui_', 'himo_phase1_icon_',
        'himo_call_', 'home_', 'icon_', 'himo_login_icon_', 'himo_signup_icon_',
        'himo_login_eye_', 'himo_storage_icon_', 'himo_download_',
    ]
    suffix = stem
    prefix_matched = False
    for prefix in prefixes:
        if stem.startswith(prefix):
            suffix = stem[len(prefix):]
            prefix_matched = True
            break
    if stem in ('eye_open','eye_closed'):
        suffix = stem
    if suffix.endswith('_neon'):
        suffix = suffix[:-5]
    if suffix.endswith('_white'):
        suffix = suffix[:-6]
    if suffix.endswith('_purple'):
        suffix = suffix[:-7]
    if suffix.startswith('icon_') and not prefix_matched:
        suffix = suffix[5:]
    semantic = ALIASES.get(suffix)
    # A few family prefixes have context-dependent suffixes.
    if semantic is None and suffix == 'more': semantic = 'more'
    return semantic

# Icons that should remain white because they sit on strongly colored media/call buttons.
WHITE_BASE = {
 'call_end_white.png','call_camera_white.png','call_camera_off_white.png','call_mic_white.png','call_mic_off_white.png',
 'call_more_white.png','call_phone_white.png','call_speaker_white.png','call_speaker_off_white.png',
 'himo_call_end.png','himo_icon_arrow_back_white.png','himo_ui_close_white.png',
 'himo_ui_play_white.png','himo_ui_pause_white.png','himo_download_white.png'
}
DANGER_BASE = {'settings_icon_delete.png','selection_delete.png','himo_ui_delete.png','himo_icon_call_end.png','himo_phase1_icon_call_end.png'}

# Make a themed SVG that uses one strong foreground color with a quiet secondary
# detail. Most icons remain outline-only; media controls keep simple solid glyphs.
def svg_for(semantic, main, dark, soft, white=False, danger=False):
    if semantic not in SHAPES:
        return None
    color = '#FFFFFF' if white else ('#E64B60' if danger else main)
    detail = '#FF8996' if danger else (soft if not white else '#FFFFFF')
    body = SHAPES[semantic].replace('{color}', color)
    # Small visual cues become a restrained second tone; main geometry remains monoline.
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="192" height="192" viewBox="0 0 24 24">
  <g fill="none" stroke="{color}" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">{body}</g>
</svg>'''
    return svg

def render_icon(path: Path, semantic, palette=DEFAULT, filename=None, themed=False):
    main, dark, soft = palette
    fname = filename or path.name
    white = (not themed and fname.lower() in WHITE_BASE) or ('_white.' in fname.lower() and themed is False)
    danger = (not themed and fname.lower() in DANGER_BASE)
    svg = svg_for(semantic, main, dark, soft, white=white, danger=danger)
    if svg is None:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    cairosvg.svg2png(bytestring=svg.encode('utf-8'), write_to=str(path), output_width=192, output_height=192)
    return True

# First create editable SVG source masters, not just raster assets.
master_index = {}
for semantic, body in SHAPES.items():
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="192" height="192" viewBox="0 0 24 24"><g fill="none" stroke="{DEFAULT[0]}" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">{body.replace('{color}', DEFAULT[0])}</g></svg>'''
    master_path = OUT_SVG / f'{semantic}.svg'
    master_path.write_text(svg, encoding='utf-8')
    master_index[semantic] = master_path.name

updated = []
missed = []
# Replace every legacy icon bitmap and every existing theme-specific variant that maps to a known semantic.
for p in IMG.glob('*.png'):
    # Don't overwrite launcher icon artwork, user avatars, wallpapers, branding, or unrelated illustrations.
    name = p.name.lower()
    if name.startswith(('himo_professional_icon_', 'himo_system_icon_', 'himo_glass_icon_')):
        continue
    semantic = semantic_from_filename(p.name)
    if semantic is None:
        continue
    themed_style = None
    for style in PALETTES:
        if name.startswith(f'himo_theme_{style}_'):
            themed_style = style
            break
    palette = PALETTES[themed_style] if themed_style else DEFAULT
    if render_icon(p, semantic, palette, filename=p.name, themed=themed_style is not None):
        updated.append(p.name)
    else:
        missed.append((p.name, semantic))

# Generate themed variants for every extra icon family handled by the appearance manager.
extra_prefixes = ('himo_login_icon_', 'himo_login_eye_', 'himo_signup_icon_', 'himo_storage_icon_', 'himo_download_')
for p in list(IMG.glob('*.png')):
    if p.name.lower().startswith('himo_theme_'):
        continue
    if not p.name.lower().startswith(extra_prefixes):
        continue
    semantic = semantic_from_filename(p.name)
    if semantic is None:
        continue
    for style, palette in PALETTES.items():
        target = IMG / f'himo_theme_{style}_{p.name}'
        render_icon(target, semantic, palette, filename=p.name, themed=True)
        updated.append(target.name)

# A compact source/readme lets future developers regenerate/update this system without editing UI files.
readme = ROOT / 'HIMO-PRO-ICON-SYSTEM-AR.md'
readme.write_text('''# نظام أيقونات Himo الاحترافي\n\n- استُبدلت رسومات الرموز القديمة بنمط موحّد بخطوط واضحة وحواف مستديرة وتناسب ثابت.\n- استُبقيت أسماء ملفات PNG المستخدمة في XAML وC# لتجنّب كسر الربط والأحداث.\n- توجد ملفات SVG المصدرية في `Design/ProfessionalIcons`.\n- صُدّرت الرموز الأساسية وكل نسخ السمات الـ14 من هذه المصادر إلى `Resources/Images`.\n- تم الإبقاء دون تعديل على أيقونات Himo الخاصة بالشاشة الرئيسية (launcher icons)، والرموز الزجاجية/النظامية، والصور الشخصية والخلفيات.\n- أيقونات الحذف وإنهاء المكالمة احتفظت بدلالتها اللونية/البيضاء عندما يلزم لسهولة التمييز.\n\nالملفات تُعرض من خلال الأسماء الحالية في صفحات التطبيق؛ لا يلزم تعديل وظائف الصفحات لتغيير رسوماتها.\n''', encoding='utf-8')

print(json.dumps({'base_and_theme_icons_updated': len(updated), 'unique_masters': len(master_index), 'masters': sorted(master_index), 'unmapped': missed[:100], 'unmapped_count': len(missed), 'output_svg_dir': str(OUT_SVG)}, ensure_ascii=False, indent=2))
