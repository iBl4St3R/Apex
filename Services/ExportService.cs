using System.IO;
using System.Text;
using System.Text.Json;
using Apex.Models;
using Markdig;

namespace Apex.Services;

public static class ExportService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseGridTables()
        .UseTaskLists()
        .UsePipeTables()
        .Build();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ──────────────────────────────────────────────
    //  Public entry point
    // ──────────────────────────────────────────────

    public static void Export(ApexProject project, string outputHtmlPath)
    {
        string projectName = project.ProjectName;
        string rootFolder  = project.RootFolder;

        // Folder na obrazki obok HTML
        string htmlDir     = Path.GetDirectoryName(outputHtmlPath)!;
        string imgFolder   = Path.Combine(htmlDir, projectName);
        Directory.CreateDirectory(imgFolder);

        // 1. Kopiuj obrazki
        CopyImages(project, rootFolder, imgFolder);

        // 2. Zbuduj JSON danych
        string dataJson = BuildDataJson(project, rootFolder, projectName);

        // 3. Złóż HTML
        string html = BuildHtml(projectName, dataJson);

        // 4. Zapisz
        File.WriteAllText(outputHtmlPath, html, Encoding.UTF8);
    }

    // ──────────────────────────────────────────────
    //  Kopiowanie obrazków
    // ──────────────────────────────────────────────

    private static void CopyImages(ApexProject project, string rootFolder, string imgFolder)
    {
        string imagesSource = Path.Combine(rootFolder, ".images");
        if (!Directory.Exists(imagesSource)) return;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ic in project.ImageCards)
        {
            string rel = ic.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            used.Add(rel);
        }

        foreach (string srcFile in Directory.EnumerateFiles(imagesSource))
        {
            string rel = ".images" + Path.DirectorySeparatorChar + Path.GetFileName(srcFile);
            if (!used.Contains(rel)) continue; // kopiuj tylko używane

            string dest = Path.Combine(imgFolder, Path.GetFileName(srcFile));
            File.Copy(srcFile, dest, overwrite: true);
        }
    }

    // ──────────────────────────────────────────────
    //  Budowanie JSON
    // ──────────────────────────────────────────────

    private static string BuildDataJson(ApexProject project, string rootFolder, string projectName)
    {
        var cardsData = project.Cards.Select(c =>
        {
            string fullPath = FileService.GetFullPath(rootFolder, c.RelativePath);
            string markdown = File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
            string htmlContent = Markdown.ToHtml(markdown, Pipeline);
            string preview = ExtractPlainText(markdown, 200);
            string modified = "";
            try { modified = new FileInfo(fullPath).LastWriteTime.ToString("yyyy-MM-dd HH:mm"); } catch { }

            return new
            {
                relativePath = c.RelativePath.Replace('\\', '/'),
                boardX       = c.BoardX,
                boardY       = c.BoardY,
                categoryId   = c.CategoryId,
                customWidth  = c.CustomWidth,
                customHeight = c.CustomHeight,
                cardSize     = c.CardSize ?? "minimum",
                locked       = c.Locked,
                htmlContent,
                preview,
                modifiedDate = modified
            };
        }).ToList();

        var titleData = project.TitleCards.Select(t => new
        {
            id              = t.Id,
            text            = t.Text,
            boardX          = t.BoardX,
            boardY          = t.BoardY,
            fontFamily      = t.FontFamily,
            fontSize        = t.FontSize,
            fontColor       = t.FontColor,
            backgroundColor = t.BackgroundColor,
            bold            = t.Bold,
            italic          = t.Italic,
            customWidth     = t.CustomWidth,
            customHeight    = t.CustomHeight,
            locked          = t.Locked
        }).ToList();

        var imageData = project.ImageCards.Select(i =>
        {
            string fname = Path.GetFileName(i.RelativePath);
            return new
            {
                id           = i.Id,
                relativePath = i.RelativePath.Replace('\\', '/'),
                imgSrc       = $"{projectName}/{fname}",
                boardX       = i.BoardX,
                boardY       = i.BoardY,
                customWidth  = i.CustomWidth,
                customHeight = i.CustomHeight,
                categoryId   = i.CategoryId,
                locked       = i.Locked
            };
        }).ToList();

        var catData = project.Categories.Select(c => new
        {
            id    = c.Id,
            name  = c.Name,
            color = c.Color
        }).ToList();

        var relData = project.Relations.Select(r => new
        {
            id            = r.Id,
            sourceType    = r.SourceType,
            sourceRef     = r.SourceRef,
            targetType    = r.TargetType,
            targetRef     = r.TargetRef,
            bendX         = r.BendX,
            bendY         = r.BendY,
            lineColor     = r.LineColor,
            lineThickness = r.LineThickness
        }).ToList();

        // Connections z [[linków]]
        var connections = new List<object>();
        foreach (var card in project.Cards)
        {
            string fullPath = FileService.GetFullPath(rootFolder, card.RelativePath);
            if (!File.Exists(fullPath)) continue;

            foreach (string linkTarget in ConnectionResolver.ParseLinks(fullPath))
            {
                var candidates = ConnectionResolver.FindCandidates(project, linkTarget);
                if (candidates.Count == 1)
                {
                    connections.Add(new
                    {
                        sourcePath = card.RelativePath.Replace('\\', '/'),
                        targetPath = candidates[0].RelativePath.Replace('\\', '/')
                    });
                }
            }
        }

        var payload = new
        {
            projectName = project.ProjectName,
            cards       = cardsData,
            titleCards  = titleData,
            imageCards  = imageData,
            categories  = catData,
            relations   = relData,
            connections
        };

        return JsonSerializer.Serialize(payload, JsonOpts);
    }

    // ──────────────────────────────────────────────
    //  Plain text preview (strip MD)
    // ──────────────────────────────────────────────

    private static string ExtractPlainText(string markdown, int maxChars)
    {
        string s = System.Text.RegularExpressions.Regex.Replace(markdown, @"\[\[([^\]]+)\]\]", "$1");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\[([^\]]*)\]\([^)]*\)", "$1");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[#*_~`>]", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > maxChars ? s[..maxChars] + "…" : s;
    }

    // ──────────────────────────────────────────────
    //  Budowanie HTML
    // ──────────────────────────────────────────────

    private static string BuildHtml(string projectName, string dataJson)
    {
        // dataJson trafia jako JS literal — escapujemy </script>
        string safeJson = dataJson.Replace("</script>", "<\\/script>");

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{{projectName}} — Apex Export</title>
<style>
*,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
:root{
  --bg:#1E1E2E;--bg2:#181825;--bg3:#11111B;--bg4:#313244;
  --surface:#313244;--surface2:#45475A;--surface3:#585B70;
  --text:#CDD6F4;--text2:#BAC2DE;--text3:#A6ADC8;--muted:#6C7086;
  --border:#313244;--border2:#45475A;
  --blue:#89B4FA;--purple:#CBA6F7;--green:#A6E3A1;
  --red:#F38BA8;--yellow:#F9E2AF;--peach:#FAB387;
  --card-w:220px;--card-h:120px;
}
html,body{width:100%;height:100%;overflow:hidden;background:var(--bg2);color:var(--text);font-family:"Segoe UI",system-ui,sans-serif;font-size:13px}

/* ── TOOLBAR ── */
#toolbar{position:fixed;top:0;left:0;right:0;height:44px;background:var(--bg3);border-bottom:1px solid var(--border);display:flex;align-items:center;gap:8px;padding:0 12px;z-index:1000}
#toolbar h1{font-size:14px;font-weight:600;color:var(--text);margin-right:8px}
.tb-btn{background:transparent;border:1px solid var(--border2);color:var(--text3);padding:4px 10px;border-radius:5px;cursor:pointer;font-size:12px;font-weight:600;transition:background .15s}
.tb-btn:hover{background:var(--surface)}
.tb-btn.active{background:var(--surface);color:var(--text)}

/* ── BOARD ── */
#board-wrap{position:fixed;top:44px;left:0;right:0;bottom:0;overflow:hidden;background:var(--bg2);cursor:grab}
#board-wrap.panning{cursor:grabbing}
#board{position:absolute;width:10000px;height:10000px;transform-origin:0 0}

/* ── SVG OVERLAY ── */
#svg-overlay{position:absolute;top:0;left:0;width:10000px;height:10000px;pointer-events:none;overflow:visible}

/* ── NOTE CARD ── */
.card{position:absolute;background:var(--bg);border:1px solid var(--border2);border-radius:8px;overflow:hidden;cursor:pointer;transition:box-shadow .15s;display:flex;user-select:none}
.card:hover{box-shadow:0 0 0 2px var(--purple)}
.card-strip{width:4px;flex-shrink:0;border-radius:8px 0 0 8px}
.card-body{flex:1;padding:8px;min-width:0;display:flex;flex-direction:column;gap:4px}
.card-title-row{display:flex;align-items:center;gap:6px;min-width:0}
.card-title{font-size:13px;font-weight:700;color:var(--text);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;flex:1}
.cat-badge{font-size:10px;font-weight:700;color:#fff;padding:2px 6px;border-radius:4px;white-space:nowrap;flex-shrink:0}
.card-folder{font-size:9px;color:var(--muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.card-preview{font-size:11px;color:var(--text3);line-height:1.4;overflow:hidden;flex:1;word-break:break-word}
.card-date{font-size:10px;color:var(--muted);text-align:right;margin-top:auto}

/* ── TITLE CARD ── */
.title-card{position:absolute;border:1px solid var(--border2);border-radius:8px;display:flex;align-items:center;justify-content:center;cursor:pointer;user-select:none;padding:8px 12px;text-align:center;overflow:hidden}
.title-card:hover{box-shadow:0 0 0 2px var(--purple)}

/* ── IMAGE CARD ── */
.img-card{position:absolute;border:1px solid var(--border2);border-radius:8px;overflow:hidden;cursor:pointer;background:var(--bg3);display:flex;align-items:center;justify-content:center}
.img-card:hover{box-shadow:0 0 0 2px var(--purple)}
.img-card img{max-width:100%;max-height:100%;object-fit:contain}

/* ── MODAL ── */
#modal-backdrop{display:none;position:fixed;inset:0;background:rgba(0,0,0,.7);z-index:2000;align-items:center;justify-content:center}
#modal-backdrop.open{display:flex}
#modal{background:var(--bg);border:1px solid var(--border2);border-radius:12px;width:min(860px,92vw);max-height:88vh;display:flex;flex-direction:column;overflow:hidden}
#modal-header{background:var(--bg3);border-bottom:1px solid var(--border);padding:10px 16px;display:flex;align-items:center;gap:10px}
#modal-title{font-size:16px;font-weight:700;color:var(--text);flex:1}
#modal-cat{font-size:11px;font-weight:700;color:#fff;padding:2px 8px;border-radius:4px}
#modal-close{background:var(--surface);border:none;color:var(--text2);width:30px;height:30px;border-radius:6px;cursor:pointer;font-size:16px;display:flex;align-items:center;justify-content:center}
#modal-close:hover{background:var(--surface2)}
#modal-body{padding:20px 24px;overflow-y:auto;flex:1}

/* ── IMAGE MODAL ── */
#imgmodal-backdrop{display:none;position:fixed;inset:0;background:rgba(0,0,0,.85);z-index:2000;align-items:center;justify-content:center;cursor:zoom-out}
#imgmodal-backdrop.open{display:flex}
#imgmodal-backdrop img{max-width:90vw;max-height:90vh;object-fit:contain;border-radius:8px;box-shadow:0 8px 40px rgba(0,0,0,.6);cursor:default}

/* ── MARKDOWN RENDER ── */
#modal-body h1{font-size:26px;font-weight:700;color:#BAC2DE;margin:16px 0 8px}
#modal-body h2{font-size:20px;font-weight:600;color:#B0BAD8;margin:14px 0 6px}
#modal-body h3{font-size:16px;font-weight:600;color:#A6B0CE;margin:12px 0 6px}
#modal-body p{color:#A6ADC8;line-height:1.7;margin-bottom:10px}
#modal-body ul,#modal-body ol{padding-left:20px;color:#A6ADC8;margin-bottom:10px;line-height:1.7}
#modal-body code{font-family:Consolas,monospace;font-size:12px;background:#18182A;color:#F38BA8;padding:1px 5px;border-radius:3px}
#modal-body pre{background:#18182A;border:1px solid var(--border2);border-radius:6px;padding:12px;overflow-x:auto;margin-bottom:12px}
#modal-body pre code{background:none;color:#CDD6F4;padding:0}
#modal-body a{color:#89B4FA;text-decoration:underline;cursor:pointer}
#modal-body blockquote{border-left:3px solid var(--border2);padding-left:12px;color:var(--muted);margin-bottom:10px}
#modal-body table{border-collapse:collapse;width:100%;margin-bottom:12px}
#modal-body th,#modal-body td{border:1px solid var(--border2);padding:6px 10px;color:var(--text2)}
#modal-body th{background:var(--bg3);color:var(--text);font-weight:600}
#modal-body hr{border:none;border-top:1px solid var(--border);margin:14px 0}
#modal-body strong{font-weight:700;color:var(--text)}
#modal-body em{font-style:italic}
#modal-body input[type=checkbox]{accent-color:var(--purple);margin-right:6px}

/* ── SCROLLBAR ── */
::-webkit-scrollbar{width:6px;height:6px}
::-webkit-scrollbar-track{background:transparent}
::-webkit-scrollbar-thumb{background:var(--surface2);border-radius:3px}
</style>
</head>
<body>

<!-- TOOLBAR -->
<div id="toolbar">
  <h1>{{projectName}}</h1>
  <button class="tb-btn" id="btn-fit" onclick="fitAll()">Fit all</button>
  <button class="tb-btn" id="btn-conn" onclick="toggleConnections()">Connections</button>
  <span style="color:var(--muted);font-size:11px;margin-left:8px">Scroll = zoom · Drag = pan · Click card = preview</span>
</div>

<!-- BOARD -->
<div id="board-wrap">
  <div id="board">
    <svg id="svg-overlay" xmlns="http://www.w3.org/2000/svg"></svg>
    <!-- cards injected by JS -->
  </div>
</div>

<!-- NOTE MODAL -->
<div id="modal-backdrop" onclick="closeModal(event)">
  <div id="modal" onclick="e=>e.stopPropagation()">
    <div id="modal-header">
      <div id="modal-title"></div>
      <div id="modal-cat" style="display:none"></div>
      <button id="modal-close" onclick="closeModal()">✕</button>
    </div>
    <div id="modal-body"></div>
  </div>
</div>

<!-- IMAGE MODAL -->
<div id="imgmodal-backdrop" onclick="closeImgModal()">
  <img id="imgmodal-img" src="" alt="">
</div>

<script>
const DATA = {{safeJson}};

// ── STATE ──────────────────────────────────────────────────────────────
let panX=0, panY=0, zoom=1;
let isPanning=false, panStartX=0, panStartY=0, panOriginX=0, panOriginY=0;
const MIN_ZOOM=0.25, MAX_ZOOM=3;
let connectionsVisible=false;
let currentModalPath=null;

const board       = document.getElementById('board');
const boardWrap   = document.getElementById('board-wrap');
const svgOverlay  = document.getElementById('svg-overlay');

// ── HELPERS ───────────────────────────────────────────────────────────
function catById(id){ return DATA.categories.find(c=>c.id===id)||null; }

function hexToRgb(hex){
  hex=hex.replace('#','');
  if(hex.length===6){
    const r=parseInt(hex.slice(0,2),16),g=parseInt(hex.slice(2,4),16),b=parseInt(hex.slice(4,6),16);
    return {r,g,b};
  }
  return {r:69,g:71,b:90};
}

function cardDimensions(c){
  if(c.customWidth && c.customHeight) return {w:c.customWidth, h:c.customHeight};
  const size = c.cardSize||'minimum';
  return {
    w: size==='large'?880:size==='medium'?440:220,
    h: size==='large'?320:size==='medium'?160:120
  };
}

// ── RENDER CARDS ──────────────────────────────────────────────────────
function buildBoard(){
  // NoteCards
  DATA.cards.forEach(c=>{
    const cat=c.categoryId?catById(c.categoryId):null;
    const stripColor=cat?cat.color:'#45475A';
    const {w,h}=cardDimensions(c);

    const el=document.createElement('div');
    el.className='card';
    el.dataset.path=c.relativePath;
    el.style.cssText=`left:${c.boardX}px;top:${c.boardY}px;width:${w}px;height:${h}px`;

    const strip=document.createElement('div');
    strip.className='card-strip';
    strip.style.background=stripColor;

    const body=document.createElement('div');
    body.className='card-body';

    // title row
    const titleRow=document.createElement('div');
    titleRow.className='card-title-row';
    const titleEl=document.createElement('div');
    titleEl.className='card-title';
    titleEl.textContent=pathToTitle(c.relativePath);
    titleRow.appendChild(titleEl);
    if(cat){
      const badge=document.createElement('span');
      badge.className='cat-badge';
      badge.style.background=cat.color;
      badge.textContent=cat.name;
      titleRow.appendChild(badge);
    }

    // folder label
    const folder=pathFolder(c.relativePath);
    if(folder){
      const fl=document.createElement('div');
      fl.className='card-folder';
      fl.textContent=folder+'/';
      body.appendChild(titleRow);
      body.appendChild(fl);
    } else {
      body.appendChild(titleRow);
    }

    // preview
    if(c.preview){
      const prev=document.createElement('div');
      prev.className='card-preview';
      prev.textContent=c.preview;
      body.appendChild(prev);
    }

    // date
    if(c.modifiedDate){
      const d=document.createElement('div');
      d.className='card-date';
      d.textContent=c.modifiedDate;
      body.appendChild(d);
    }

    el.appendChild(strip);
    el.appendChild(body);
    el.addEventListener('click',()=>openModal(c));
    board.appendChild(el);
  });

  // TitleCards
  DATA.titleCards.forEach(t=>{
    const w=t.customWidth||300, h=t.customHeight||80;
    const el=document.createElement('div');
    el.className='title-card';
    el.style.cssText=`left:${t.boardX}px;top:${t.boardY}px;width:${w}px;height:${h}px;background:${t.backgroundColor};color:${t.fontColor};font-family:${t.fontFamily},sans-serif;font-size:${Math.min(t.fontSize,64)}px;font-weight:${t.bold?700:400};font-style:${t.italic?'italic':'normal'}`;
    el.textContent=t.text;
    board.appendChild(el);
  });

  // ImageCards
  DATA.imageCards.forEach(ic=>{
    const w=ic.customWidth||300, h=ic.customHeight||200;
    const el=document.createElement('div');
    el.className='img-card';
    el.style.cssText=`left:${ic.boardX}px;top:${ic.boardY}px;width:${w}px;height:${h}px`;
    const img=document.createElement('img');
    img.src=ic.imgSrc;
    img.alt='';
    img.style.cssText='max-width:100%;max-height:100%;object-fit:contain';
    el.appendChild(img);
    el.addEventListener('click',()=>openImgModal(ic.imgSrc));
    board.appendChild(el);
  });

  renderRelations();
}

function pathToTitle(p){ return p.split('/').pop().replace(/\.md$/i,''); }
function pathFolder(p){
  const parts=p.replace(/\.md$/i,'').split('/');
  return parts.length>1?parts.slice(0,-1).join('/'):null;
}

// ── RELATIONS SVG ─────────────────────────────────────────────────────
function cardCenter(type,ref){
  let el=null;
  if(type==='note'){
    el=board.querySelector(`.card[data-path="${CSS.escape(ref)}"]`);
  } else if(type==='title'){
    el=board.querySelectorAll('.title-card')[DATA.titleCards.findIndex(t=>t.id===ref)];
  } else if(type==='image'){
    el=board.querySelectorAll('.img-card')[DATA.imageCards.findIndex(i=>i.id===ref)];
  }
  if(!el) return null;
  const x=parseFloat(el.style.left||0);
  const y=parseFloat(el.style.top||0);
  const w=parseFloat(el.style.width||220);
  const h=parseFloat(el.style.height||120);
  return {x:x+w/2, y:y+h/2};
}

function makeArrowHead(svg, x1,y1,x2,y2, color, thickness){
  const angle=Math.atan2(y2-y1,x2-x1);
  const scale=Math.max(0.7,Math.min(3,thickness/1.5));
  const aLen=11*scale, aW=5*scale;
  const ax=x2-aLen*Math.cos(angle), ay=y2-aLen*Math.sin(angle);
  const p=document.createElementNS('http://www.w3.org/2000/svg','polygon');
  const pts=[
    `${x2},${y2}`,
    `${ax-aW*Math.sin(angle)},${ay+aW*Math.cos(angle)}`,
    `${ax+aW*Math.sin(angle)},${ay-aW*Math.cos(angle)}`
  ];
  p.setAttribute('points',pts.join(' '));
  const rgb=hexToRgb(color);
  p.setAttribute('fill',`rgba(${rgb.r},${rgb.g},${rgb.b},0.8)`);
  svg.appendChild(p);
}

function renderRelations(){
  // Clear old relation/connection elements
  svgOverlay.querySelectorAll('.rel,.conn').forEach(e=>e.remove());

  // Relations (user-drawn arrows)
  DATA.relations.forEach(r=>{
    const src=cardCenter(r.sourceType,r.sourceRef);
    const tgt=cardCenter(r.targetType,r.targetRef);
    if(!src||!tgt) return;

    const midX=(src.x+tgt.x)/2+r.bendX;
    const midY=(src.y+tgt.y)/2+r.bendY;
    const color=r.lineColor||'#CBA6F7';
    const thick=r.lineThickness||1.5;
    const rgb=hexToRgb(color);

    const path=document.createElementNS('http://www.w3.org/2000/svg','path');
    path.setAttribute('d',`M${src.x},${src.y} Q${midX},${midY} ${tgt.x},${tgt.y}`);
    path.setAttribute('fill','none');
    path.setAttribute('stroke',`rgba(${rgb.r},${rgb.g},${rgb.b},0.65)`);
    path.setAttribute('stroke-width',thick);
    path.classList.add('rel');
    svgOverlay.insertBefore(path,svgOverlay.firstChild);

    makeArrowHead(svgOverlay,midX,midY,tgt.x,tgt.y,color,thick);
    svgOverlay.lastElementChild.classList.add('rel');
  });

  // Connections ([[links]])
  if(connectionsVisible){
    DATA.connections.forEach(cn=>{
      const src=cardCenter('note',cn.sourcePath);
      const tgt=cardCenter('note',cn.targetPath);
      if(!src||!tgt) return;

      const line=document.createElementNS('http://www.w3.org/2000/svg','line');
      line.setAttribute('x1',src.x); line.setAttribute('y1',src.y);
      line.setAttribute('x2',tgt.x); line.setAttribute('y2',tgt.y);
      line.setAttribute('stroke','rgba(137,180,250,0.45)');
      line.setAttribute('stroke-width','1.5');
      line.classList.add('conn');
      svgOverlay.insertBefore(line,svgOverlay.firstChild);

      makeArrowHead(svgOverlay,src.x,src.y,tgt.x,tgt.y,'#89B4FA',1.5);
      svgOverlay.lastElementChild.classList.add('conn');
    });
  }
}

// ── PAN ───────────────────────────────────────────────────────────────
boardWrap.addEventListener('mousedown',e=>{
  if(e.button!==0 && e.button!==1) return;
  // Don't pan when clicking a card
  if(e.target.closest('.card,.title-card,.img-card')) return;
  isPanning=true;
  boardWrap.classList.add('panning');
  panStartX=e.clientX; panStartY=e.clientY;
  panOriginX=panX; panOriginY=panY;
  e.preventDefault();
});
window.addEventListener('mousemove',e=>{
  if(!isPanning) return;
  panX=panOriginX+(e.clientX-panStartX);
  panY=panOriginY+(e.clientY-panStartY);
  applyTransform();
});
window.addEventListener('mouseup',()=>{ isPanning=false; boardWrap.classList.remove('panning'); });

// ── ZOOM ──────────────────────────────────────────────────────────────
boardWrap.addEventListener('wheel',e=>{
  e.preventDefault();
  const delta=e.deltaY>0?-0.08:0.08;
  const newZoom=Math.max(MIN_ZOOM,Math.min(MAX_ZOOM,zoom+delta));
  if(Math.abs(newZoom-zoom)<0.001) return;

  const rect=boardWrap.getBoundingClientRect();
  const mx=e.clientX-rect.left, my=e.clientY-rect.top;
  const canvasX=(mx-panX)/zoom, canvasY=(my-panY)/zoom;
  zoom=newZoom;
  panX=mx-canvasX*zoom; panY=my-canvasY*zoom;
  applyTransform();
},{passive:false});

function applyTransform(){
  board.style.transform=`translate(${panX}px,${panY}px) scale(${zoom})`;
  board.style.transformOrigin='0 0';
}

// ── FIT ALL ───────────────────────────────────────────────────────────
function fitAll(){
  const vpw=boardWrap.clientWidth, vph=boardWrap.clientHeight;
  if(!DATA.cards.length && !DATA.imageCards.length && !DATA.titleCards.length) return;

  let minX=Infinity,minY=Infinity,maxX=-Infinity,maxY=-Infinity;
  function expand(x,y,w,h){ minX=Math.min(minX,x);minY=Math.min(minY,y);maxX=Math.max(maxX,x+w);maxY=Math.max(maxY,y+h); }

  DATA.cards.forEach(c=>{ const {w,h}=cardDimensions(c); expand(c.boardX,c.boardY,w,h); });
  DATA.imageCards.forEach(i=>expand(i.boardX,i.boardY,i.customWidth||300,i.customHeight||200));
  DATA.titleCards.forEach(t=>expand(t.boardX,t.boardY,t.customWidth||300,t.customHeight||80));

  const bw=maxX-minX, bh=maxY-minY;
  const z=Math.min(Math.max(MIN_ZOOM,(vpw-80)/bw),Math.max(MIN_ZOOM,(vph-80)/bh),MAX_ZOOM);
  zoom=z;
  panX=(vpw-bw*z)/2-minX*z;
  panY=(vph-bh*z)/2-minY*z;
  applyTransform();
}

// ── CONNECTIONS TOGGLE ────────────────────────────────────────────────
function toggleConnections(){
  connectionsVisible=!connectionsVisible;
  document.getElementById('btn-conn').classList.toggle('active',connectionsVisible);
  renderRelations();
}

// ── NOTE MODAL ────────────────────────────────────────────────────────
function openModal(card){
  currentModalPath=card.relativePath;
  const cat=card.categoryId?catById(card.categoryId):null;

  document.getElementById('modal-title').textContent=pathToTitle(card.relativePath);

  const catEl=document.getElementById('modal-cat');
  if(cat){ catEl.style.display='inline-block'; catEl.style.background=cat.color; catEl.textContent=cat.name; }
  else catEl.style.display='none';

  const body=document.getElementById('modal-body');
  body.innerHTML=card.htmlContent||'';

  // Make [[links]] clickable in modal
  body.querySelectorAll('a').forEach(a=>{
    const href=a.getAttribute('href')||'';
    if(href.startsWith('#') || href===''){ // potential wiki-link placeholder
      a.addEventListener('click',ev=>{
        ev.preventDefault();
        const text=a.textContent;
        const target=DATA.cards.find(c=>pathToTitle(c.relativePath)===text);
        if(target) openModal(target);
      });
    } else {
      a.setAttribute('target','_blank');
      a.setAttribute('rel','noopener');
    }
  });

  // Handle [[link]] patterns that Markdig turns into plain text
  // Markdig renders [[X]] as-is in paragraphs — we post-process
  body.querySelectorAll('p,li').forEach(node=>{
    node.innerHTML=node.innerHTML.replace(/\[\[([^\]]+)\]\]/g,(m,name)=>{
      return `<a class="wiki-link" data-target="${name}" style="color:#89B4FA;cursor:pointer" href="#">${name}</a>`;
    });
  });
  body.querySelectorAll('.wiki-link').forEach(a=>{
    a.addEventListener('click',ev=>{
      ev.preventDefault();
      const name=a.dataset.target;
      const target=DATA.cards.find(c=>pathToTitle(c.relativePath)===name);
      if(target) openModal(target);
    });
  });

  document.getElementById('modal-backdrop').classList.add('open');
}

function closeModal(e){
  if(e && e.target!==document.getElementById('modal-backdrop')) return;
  document.getElementById('modal-backdrop').classList.remove('open');
  currentModalPath=null;
}

// ── IMAGE MODAL ───────────────────────────────────────────────────────
function openImgModal(src){
  document.getElementById('imgmodal-img').src=src;
  document.getElementById('imgmodal-backdrop').classList.add('open');
}
function closeImgModal(){ document.getElementById('imgmodal-backdrop').classList.remove('open'); }

// ── KEYBOARD ──────────────────────────────────────────────────────────
document.addEventListener('keydown',e=>{
  if(e.key==='Escape'){
    closeModal({target:document.getElementById('modal-backdrop')});
    closeImgModal();
  }
});

// ── INIT ──────────────────────────────────────────────────────────────
buildBoard();
requestAnimationFrame(fitAll);
</script>
</body>
</html>
""";
    }
}