import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const outDir = dirname(fileURLToPath(import.meta.url));
mkdirSync(outDir, { recursive: true });

const C = {
  sky: '#1D3F55', skyDeep: '#112C3E', ocean: '#0D5260', oceanDeep: '#082F3B',
  deck: '#754426', deckDark: '#3C2419', deckLight: '#A66B38', rail: '#D2AD69',
  surface: '#232334', raised: '#2C2B3E', inset: '#191A28', strong: '#473957',
  ink: '#FFF8E8', muted: '#C7C3C3', border: '#D7A24B', borderSoft: '#796146',
  primary: '#37AEB0', onPrimary: '#071C21', secondary: '#B68AE1',
  gold: '#FFD46C', goldBright: '#F1BD3A', hp: '#EF6471', xp: '#F4C64D',
  mp: '#69B9F2', success: '#70D6A2', danger: '#FF8291', focus: '#FCE36E',
  daySky: '#8BD2DF', dayDeep: '#3B98B5', dayOcean: '#087A8D', daySurface: '#FFF7DF',
  dayRaised: '#FFFDF5', dayInset: '#EFE0BB', dayInk: '#1D2630', dayMuted: '#53616B',
  dayBorder: '#58371F', dayPrimary: '#0A6F78', white: '#FFFFFF', black: '#071014'
};

const esc = (s) => String(s).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
const fmt = (n) => Number(n.toFixed(2));
const pt = (x, y) => `${fmt(x)},${fmt(y)}`;

function notchedPoints(x, y, w, h, n = 8) {
  return [pt(x+n,y),pt(x+w-n,y),pt(x+w-n/2,y+n/2),pt(x+w,y+n/2),pt(x+w,y+h-n),pt(x+w-n,y+h-n),pt(x+w-n,y+h),pt(x+n,y+h),pt(x+n,y+h-n/2),pt(x,y+h-n/2),pt(x,y+n),pt(x+n,y+n)].join(' ');
}

function rect(x,y,w,h,fill,stroke='none',sw=0,rx=0,extra='') {
  return `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${rx}" fill="${fill}" stroke="${stroke}" stroke-width="${sw}" ${extra}/>`;
}
function line(x1,y1,x2,y2,stroke=C.borderSoft,sw=2,dash='') {
  return `<line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" stroke="${stroke}" stroke-width="${sw}" ${dash ? `stroke-dasharray="${dash}"` : ''}/>`;
}
function poly(points,fill,stroke='none',sw=0,extra='') {
  return `<polygon points="${points}" fill="${fill}" stroke="${stroke}" stroke-width="${sw}" ${extra}/>`;
}
function text(x,y,value,size=16,fill=C.ink,weight=500,anchor='start',family='Inter',extra='') {
  return `<text x="${x}" y="${y}" fill="${fill}" font-family="${family}" font-size="${size}" font-weight="${weight}" text-anchor="${anchor}" dominant-baseline="middle" ${extra}>${esc(value)}</text>`;
}
function mono(x,y,value,size=16,fill=C.ink,weight=700,anchor='start',extra='') {
  return text(x,y,value,size,fill,weight,anchor,'Cascadia Mono',extra);
}
function group(content, extra='') { return `<g ${extra}>${content}</g>`; }
function frame(x,y,w,h,{fill=C.surface,stroke=C.border,shadow=C.deckDark,n=8,inner=true}={}) {
  const main = notchedPoints(x,y,w,h,n);
  const sh = notchedPoints(x+6,y+7,w,h,n);
  return `${poly(sh,shadow)}${poly(main,fill,stroke,2)}${inner ? poly(notchedPoints(x+5,y+5,w-10,h-10,Math.max(4,n-2)),'none',C.borderSoft,1) : ''}`;
}
function tinyPixelIcon(x,y,kind,color=C.gold) {
  const s = 4;
  const cells = {
    clock:[[1,0],[2,0],[0,1],[3,1],[0,2],[2,2],[3,2],[0,3],[1,3],[2,3],[3,3]],
    play:[[0,0],[0,1],[1,1],[0,2],[1,2],[2,2],[0,3],[1,3],[0,4]],
    reset:[[1,0],[2,0],[0,1],[0,2],[1,2],[2,2],[2,3],[1,4],[2,4]],
    float:[[0,0],[1,0],[2,0],[3,0],[0,1],[3,1],[0,2],[3,2],[0,3],[1,3],[2,3],[3,3],[2,1]],
    chart:[[0,3],[0,4],[1,2],[1,4],[2,0],[2,4],[3,1],[3,4]],
    gear:[[1,0],[2,0],[0,1],[1,1],[2,1],[3,1],[0,2],[1,2],[2,2],[3,2],[1,3],[2,3]],
    close:[[0,0],[3,0],[1,1],[2,1],[1,2],[2,2],[0,3],[3,3]],
    pause:[[0,0],[2,0],[0,1],[2,1],[0,2],[2,2],[0,3],[2,3]],
    check:[[0,2],[1,3],[2,2],[3,1],[4,0]],
    rec:[[0,1],[1,0],[2,0],[3,1],[3,2],[2,3],[1,3],[0,2]],
  };
  return (cells[kind] || cells.clock).map(([cx,cy])=>rect(x+cx*s,y+cy*s,s,s,color)).join('');
}
function label(x,y,value,fill=C.gold) { return mono(x,y,value.toUpperCase(),12,fill,700,'start','letter-spacing="1.4"'); }
function button(x,y,w,h,value,{variant='secondary',icon=null,disabled=false}={}) {
  const fill = disabled ? C.inset : variant==='primary' ? C.gold : variant==='danger' ? C.danger : variant==='ghost' ? C.surface : C.raised;
  const ink = disabled ? C.borderSoft : variant==='primary' ? C.onPrimary : variant==='danger' ? C.black : C.ink;
  const border = disabled ? C.borderSoft : variant==='primary' ? C.goldBright : variant==='danger' ? C.danger : C.border;
  let z = rect(x+3,y+4,w,h,C.deckDark,'none',0,4)+rect(x,y,w,h,fill,border,2,4);
  if(icon) z += tinyPixelIcon(x+14,y+(h-20)/2,icon,ink);
  z += mono(x+(icon?42:w/2),y+h/2+1,value,14,ink,800,icon?'start':'middle');
  return z;
}
function field(x,y,w,labelText,value,{icon=null,chevron=true}={}) {
  let z = label(x,y,labelText,C.muted);
  z += rect(x,y+14,w,44,C.inset,C.borderSoft,2,3);
  if(icon) z += tinyPixelIcon(x+14,y+27,icon,C.gold);
  z += mono(x+(icon?42:14),y+36,value,14,C.ink,600);
  if(chevron) z += poly(`${pt(x+w-22,y+31)} ${pt(x+w-10,y+31)} ${pt(x+w-16,y+38)}`,C.gold);
  return z;
}
function toggle(x,y,on=true,labelText='',sub='') {
  let z='';
  if(labelText) z += text(x,y,labelText,14,C.ink,650);
  if(sub) z += text(x,y+18,sub,11,C.muted,450);
  const tx=x+210, ty=y-13;
  z += rect(tx,ty,46,26,on?C.primary:C.inset,on?C.gold:C.borderSoft,2,3);
  z += rect(on?tx+24:tx+4,ty+4,18,18,on?C.gold:C.muted,'none',0,2);
  return z;
}
function checkbox(x,y,on,labelText) {
  let z=rect(x,y-10,22,22,on?C.primary:C.inset,on?C.gold:C.borderSoft,2,2);
  if(on) z+=tinyPixelIcon(x+3,y-4,'check',C.gold);
  z+=text(x+32,y+1,labelText,14,C.ink,550);
  return z;
}
function tab(x,y,w,value,active=false) {
  const fill=active?C.strong:C.inset;
  let z=rect(x,y,w,42,fill,active?C.gold:C.borderSoft,2,3);
  if(active) z+=rect(x+8,y+36,w-16,4,C.gold);
  z+=mono(x+w/2,y+21,value,13,active?C.gold:C.muted,750,'middle');
  return z;
}
function badge(x,y,value,color=C.gold) {
  const w=Math.max(56,value.length*8+22);
  return rect(x,y,w,24,C.inset,color,1,2)+mono(x+w/2,y+12,value,11,color,750,'middle');
}
function sectionHeader(x,y,titleText,caption='') {
  let z=tinyPixelIcon(x,y-10,'clock',C.gold)+mono(x+28,y,titleText,18,C.gold,800);
  if(caption) z+=text(x+28,y+23,caption,12,C.muted,450);
  return z;
}
function background(w,h,titleText,subtitle='') {
  return `${rect(0,0,w,h,C.skyDeep)}<defs>
    <pattern id="seaGrid" width="16" height="16" patternUnits="userSpaceOnUse"><rect width="16" height="16" fill="${C.skyDeep}"/><rect x="0" y="0" width="8" height="8" fill="${C.sky}" opacity="0.14"/><rect x="8" y="8" width="8" height="8" fill="${C.ocean}" opacity="0.22"/></pattern>
    <pattern id="stripes" width="12" height="12" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width="6" height="12" fill="${C.gold}" opacity="0.18"/></pattern>
    <linearGradient id="goldBar" x1="0" x2="1"><stop offset="0" stop-color="${C.goldBright}"/><stop offset="1" stop-color="${C.gold}"/></linearGradient>
  </defs>${rect(0,0,w,h,'url(#seaGrid)')}${rect(0,0,w,72,C.deckDark)}${rect(0,72,w,8,C.rail)}${rect(0,80,w,5,C.deckLight)}${tinyPixelIcon(28,24,'clock',C.gold)}${mono(56,36,titleText,23,C.gold,800)}${subtitle?text(w-28,36,subtitle,13,C.rail,550,'end'):''}`;
}
function svg(w,h,titleText,body) {
  return `<?xml version="1.0" encoding="UTF-8"?>\n<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" role="img" aria-labelledby="title desc"><title id="title">${esc(titleText)}</title><desc id="desc">Editable vector UI concept for the Stopwatch Overlay pixel deck theme.</desc>${body}</svg>\n`;
}

function settingsBoard() {
  const w=1180,h=900;
  let z=background(w,h,'CHRONODECK','CONTROLLER / NIGHT DECK');
  z+=button(968,17,48,40,'_',{variant:'ghost'});
  z+=button(1024,17,48,40,'□',{variant:'ghost'});
  z+=button(1080,17,72,40,'CLOSE',{variant:'danger'});

  // main timer deck
  z+=frame(24,108,742,430,{fill:C.surface,n:10});
  z+=sectionHeader(48,138,'Timer Console','Select a timing mode, then launch a focused voyage.');
  z+=tab(48,186,158,'STOPWATCH',true)+tab(212,186,126,'CLOCK')+tab(344,186,150,'COUNTDOWN')+tab(500,186,156,'TIMECODE');
  z+=rect(48,244,694,154,C.inset,C.border,2,5);
  z+=rect(56,252,678,138,'url(#stripes)',C.borderSoft,1,3);
  z+=label(72,271,'CURRENT RUN',C.primary)+badge(624,260,'REC LIVE',C.hp);
  z+=mono(395,329,'00:14:27.6',62,C.ink,800,'middle','letter-spacing="1.5"');
  z+=text(395,374,'Deep Work Sprint  •  Project: Stopwatch',13,C.muted,500,'middle');
  z+=button(48,418,218,62,'START',{variant:'primary',icon:'play'});
  z+=button(278,418,148,62,'RESET',{icon:'reset'});
  z+=button(438,418,170,62,'FLOAT',{icon:'float'});
  z+=button(620,418,122,62,'LAP',{icon:'clock'});
  z+=checkbox(50,508,true,'Auto-start overlay')+checkbox(270,508,true,'Show REC dot')+checkbox(442,508,false,'Click-through')+checkbox(602,508,true,'Blink colon');

  // settings panel
  z+=frame(790,108,366,650,{fill:C.surface,n:10});
  z+=sectionHeader(814,138,'Settings','Deck controls and appearance.');
  z+=field(814,184,318,'THEME','Night Deck');
  z+=field(814,254,318,'DISPLAY','Display 1  •  1920 × 1080');
  z+=field(814,324,318,'POSITION','Top right');
  z+=line(814,392,1132,392,C.borderSoft,1,'4 4');
  z+=label(814,414,'APPEARANCE');
  z+=text(814,447,'Text color',14,C.ink,600)+rect(1034,434,44,28,C.ink,C.border,2,3)+mono(1128,448,'#FFF8E8',11,C.muted,600,'end');
  z+=text(814,487,'Outline',14,C.ink,600)+rect(1034,474,44,28,C.black,C.border,2,3)+mono(1128,488,'#071014',11,C.muted,600,'end');
  z+=field(814,518,152,'FONT','Cascadia Mono')+field(980,518,152,'FORMAT','HH:MM:SS.t');
  z+=field(814,588,152,'SIZE','48 px')+field(980,588,152,'OUTLINE','3 px');
  z+=toggle(814,681,true,'Light ring','Ambient edge pulse');
  z+=button(814,716,318,36,'OPEN PROJECT REPORTS',{icon:'chart'});

  // lap table
  z+=frame(24,564,742,194,{fill:C.surface,n:10});
  z+=sectionHeader(48,594,'Lap Times','Four most recent splits');
  z+=label(48,636,'LAP')+label(132,636,'SPLIT')+label(310,636,'TOTAL')+label(522,636,'NOTE');
  z+=line(48,651,742,651,C.borderSoft,1);
  const rows=[['04','03:42.8','14:27.6','Layout pass'],['03','03:58.1','10:44.8','Dashboard'],['02','03:31.5','06:46.7','Components']];
  rows.forEach((r,i)=>{const yy=674+i*25; z+=mono(48,yy,r[0],12,C.gold,750)+mono(132,yy,r[1],12,C.ink,600)+mono(310,yy,r[2],12,C.ink,600)+text(522,yy,r[3],12,C.muted,500);});

  // footer
  z+=rect(0,790,w,110,C.oceanDeep);
  z+=rect(0,790,w,5,C.rail);
  z+=badge(24,815,'RUNNING',C.success)+mono(110,827,'Deep Work Sprint',13,C.ink,700);
  z+=text(24,860,'SPACE',11,C.gold,750)+text(80,860,'Start / Pause',11,C.muted,500)+text(210,860,'CTRL + R',11,C.gold,750)+text(282,860,'Reset',11,C.muted,500)+text(360,860,'CTRL + SHIFT + O',11,C.gold,750)+text(510,860,'Overlay',11,C.muted,500);
  z+=mono(1150,846,'v1.0  •  READY',12,C.rail,700,'end');
  return svg(w,h,'Chronodeck Settings Controller',z);
}

function metricCard(x,y,w,titleText,value,delta,color,icon) {
  let z=frame(x,y,w,120,{fill:C.raised,n:7});
  z+=tinyPixelIcon(x+18,y+18,icon,color)+label(x+48,y+26,titleText,C.muted);
  z+=mono(x+18,y+72,value,29,C.ink,800);
  z+=badge(x+w-90,y+72,delta,color);
  return z;
}
function dashboardBoard() {
  const w=1280,h=900;
  let z=background(w,h,'VOYAGE REPORTS','PROJECT TIME / NIGHT DECK');
  z+=button(1110,17,142,40,'BACK TO CLOCK',{icon:'clock'});
  z+=frame(24,104,1232,92,{fill:C.surface,n:9});
  z+=field(44,122,250,'PROJECT','All projects');
  z+=field(310,122,220,'DATE RANGE','Last 7 days');
  z+=tab(576,128,96,'TODAY')+tab(678,128,88,'7 DAYS',true)+tab(772,128,96,'30 DAYS')+tab(874,128,110,'ALL TIME');
  z+=button(1032,128,198,44,'EXPORT REPORT',{variant:'primary',icon:'chart'});

  z+=metricCard(24,220,284,'TRACKED TIME','31h 42m','+12%',C.success,'clock');
  z+=metricCard(340,220,284,'SESSIONS','48','+8',C.gold,'rec');
  z+=metricCard(656,220,284,'PROJECTS','6','2 active',C.mp,'float');
  z+=metricCard(972,220,284,'FOCUS RATE','82%','+5%',C.secondary,'chart');

  // project chart
  z+=frame(24,364,748,272,{fill:C.surface,n:9});
  z+=sectionHeader(48,394,'Time by Project','Share of tracked hours across the selected range.');
  const projects=[['Stopwatch Overlay',0.83,'13h 14m',C.gold],['Habitica Bot',0.61,'9h 42m',C.primary],['Client Portal',0.35,'5h 31m',C.secondary],['Research',0.21,'3h 15m',C.mp]];
  projects.forEach((p,i)=>{const yy=456+i*41; z+=text(48,yy,p[0],13,C.ink,600); z+=rect(230,yy-10,430,20,C.inset,C.borderSoft,1,2); z+=rect(230,yy-10,430*p[1],20,p[3],'none',0,2); z+=rect(230,yy-10,430*p[1],20,'url(#stripes)','none',0,2); z+=mono(742,yy,p[2],12,C.muted,650,'end');});
  z+=label(48,610,'TOP PROJECT')+mono(164,610,'STOPWATCH OVERLAY  •  41.7%',12,C.gold,750);

  // daily totals
  z+=frame(796,364,460,272,{fill:C.surface,n:9});
  z+=sectionHeader(820,394,'Daily Totals','Focused minutes by day');
  z+=line(832,579,1228,579,C.borderSoft,1);
  const vals=[92,148,184,126,216,168,198]; const days=['M','T','W','T','F','S','S'];
  vals.forEach((v,i)=>{const bx=842+i*55; const bh=v*.62; z+=rect(bx,579-bh,32,bh,i===4?C.gold:C.primary,C.border,1,2); z+=rect(bx,579-bh,32,bh,'url(#stripes)'); z+=mono(bx+16,598,days[i],11,C.muted,700,'middle');});
  z+=line(832,444,1228,444,C.borderSoft,1,'3 5')+text(1228,432,'4h',10,C.muted,500,'end');

  // timeline and sessions
  z+=frame(24,662,478,214,{fill:C.surface,n:9});
  z+=sectionHeader(48,692,'Today Timeline','10:00 — 18:00');
  z+=rect(48,744,430,42,C.inset,C.borderSoft,2,3);
  z+=rect(90,750,78,30,C.gold)+rect(180,750,54,30,C.primary)+rect(258,750,104,30,C.secondary)+rect(390,750,66,30,C.mp);
  z+=mono(48,810,'10',10,C.muted,650)+mono(155,810,'12',10,C.muted,650)+mono(262,810,'14',10,C.muted,650)+mono(369,810,'16',10,C.muted,650)+mono(478,810,'18',10,C.muted,650,'end');
  z+=badge(48,832,'FOCUS',C.gold)+badge(120,832,'MEETING',C.primary)+badge(212,832,'BUILD',C.secondary)+badge(286,832,'REVIEW',C.mp);

  z+=frame(526,662,730,214,{fill:C.surface,n:9});
  z+=sectionHeader(550,692,'Session Log','Latest completed sessions');
  z+=label(550,738,'START')+label(636,738,'PROJECT')+label(850,738,'DURATION')+label(958,738,'MODE')+label(1124,738,'STATUS');
  z+=line(550,753,1230,753,C.borderSoft,1);
  const logs=[['16:20','Stopwatch Overlay','01:38:22','STOPWATCH','SAVED'],['14:03','Client Portal','00:52:14','COUNTDOWN','SAVED'],['11:17','Habitica Bot','01:12:09','STOPWATCH','SAVED']];
  logs.forEach((r,i)=>{const yy=776+i*27; z+=mono(550,yy,r[0],11,C.muted,650)+text(636,yy,r[1],12,C.ink,600)+mono(850,yy,r[2],11,C.ink,650)+badge(958,yy-12,r[3],i===1?C.secondary:C.primary)+badge(1124,yy-12,r[4],C.success);});
  return svg(w,h,'Voyage Reports Dashboard',z);
}

function floatingBoard() {
  const w=1040,h=600;
  let z=background(w,h,'FLOATING CLOCK STATES','COMPACT / HOVER / CLICK-THROUGH');
  z+=text(28,110,'ALWAYS-ON-TOP OVERLAY',13,C.rail,700)+text(1012,110,'100% EDITABLE VECTOR',12,C.rail,600,'end');

  // compact
  z+=label(54,160,'P1  •  COMPACT / IDLE');
  z+=poly(notchedPoints(60,204,420,104,10),C.black,C.border,2);
  z+=poly(notchedPoints(66,210,408,92,7),C.surface,C.borderSoft,1);
  z+=tinyPixelIcon(84,232,'rec',C.hp)+mono(112,242,'REC',11,C.hp,800);
  z+=mono(270,258,'00:14:27.6',39,C.ink,800,'middle');
  z+=text(270,289,'DEEP WORK SPRINT',10,C.muted,650,'middle');
  z+=rect(444,218,12,12,C.success,C.gold,1,2);

  // click through
  z+=label(554,160,'P2  •  CLICK-THROUGH / MINIMAL');
  z+=poly(notchedPoints(560,204,420,104,10),'#071014E6',C.primary,2);
  z+=mono(770,250,'00:14:27.6',43,C.ink,800,'middle');
  z+=rect(584,238,12,12,C.hp,C.gold,1,2);
  z+=label(584,275,'LOCKED',C.primary)+label(956,275,'75%',C.muted);

  // expanded hover
  z+=label(54,350,'P3  •  HOVER TOOLBAR / ACTIVE');
  z+=poly(notchedPoints(60,394,920,156,10),C.black,C.border,2);
  z+=poly(notchedPoints(66,400,908,144,7),C.surface,C.borderSoft,1);
  z+=rect(66,400,908,36,C.deckDark);
  z+=tinyPixelIcon(84,410,'clock',C.gold)+mono(112,418,'CHRONODECK',13,C.gold,800);
  z+=badge(222,406,'RUNNING',C.success)+text(950,418,'DISPLAY 1',11,C.rail,600,'end');
  z+=rect(82,452,522,74,C.inset,C.borderSoft,1,3)+mono(343,486,'00:14:27.6',45,C.ink,800,'middle');
  z+=button(624,455,96,60,'PAUSE',{icon:'pause'});
  z+=button(732,455,96,60,'RESET',{icon:'reset'});
  z+=button(840,455,112,60,'CLOSE',{variant:'danger',icon:'close'});
  return svg(w,h,'Floating Clock States',z);
}

function foundationsBoard() {
  const w=1280,h=960;
  let z=background(w,h,'PIXEL DECK FOUNDATIONS','TOKENS / NIGHT + DAY');
  z+=frame(24,108,1232,222,{fill:C.surface,n:9});
  z+=sectionHeader(48,138,'Color System','Night Deck is the default; Day Deck is a complete accessible counterpart.');
  const night=[['SKY',C.sky],['OCEAN',C.ocean],['DECK',C.deck],['SURFACE',C.surface],['RAISED',C.raised],['INK',C.ink],['BORDER',C.border],['PRIMARY',C.primary],['GOLD',C.gold],['DANGER',C.danger]];
  night.forEach((s,i)=>{const x=48+i*116; z+=rect(x,192,96,68,s[1],C.border,2,3); z+=mono(x+48,274,s[0],10,C.muted,700,'middle'); z+=mono(x+48,294,s[1],9,C.rail,600,'middle');});

  z+=frame(24,356,602,264,{fill:C.surface,n:9});
  z+=sectionHeader(48,386,'Typography','Two families, deliberately strict.');
  z+=label(48,442,'DISPLAY / CASCADIA MONO');
  z+=mono(48,482,'00:14:27.6',44,C.ink,800);
  z+=mono(48,526,'SECTION LABEL',16,C.gold,750);
  z+=label(48,560,'BODY / INTER');
  z+=text(48,590,'Project tracking stays legible at a glance.',16,C.ink,600);

  z+=frame(650,356,606,264,{fill:C.surface,n:9});
  z+=sectionHeader(674,386,'Spacing + Geometry','Built on a 4px base grid.');
  const spaces=[4,8,12,16,24,32,48];
  spaces.forEach((s,i)=>{const x=674+i*76; z+=rect(x,448,s,28,C.gold); z+=mono(x,500,`${s}px`,11,C.muted,650);});
  z+=label(674,542,'RULES');
  z+=text(674,574,'2px borders  •  6px stepped corners  •  48px touch targets',13,C.ink,600);
  z+=text(674,598,'Hard shadows: 3px controls / 6×7px panels',13,C.muted,500);

  z+=frame(24,646,602,278,{fill:C.surface,n:9});
  z+=sectionHeader(48,676,'Day Deck Palette','Reference-compatible light theme.');
  const day=[['SKY',C.daySky],['OCEAN',C.dayOcean],['SURFACE',C.daySurface],['RAISED',C.dayRaised],['INSET',C.dayInset],['INK',C.dayInk],['PRIMARY',C.dayPrimary],['BORDER',C.dayBorder]];
  day.forEach((s,i)=>{const x=48+(i%4)*134, y=734+Math.floor(i/4)*78; z+=rect(x,y,112,44,s[1],C.dayBorder,2,3); z+=mono(x+56,y+58,`${s[0]} ${s[1]}`,9,C.muted,650,'middle');});

  z+=frame(650,646,606,278,{fill:C.surface,n:9});
  z+=sectionHeader(674,676,'Interaction Contract','Familiar clock behavior in a game-like shell.');
  const rules=[['PRIMARY','Gold fill; one dominant action per panel.'],['FOCUS','2px focus ring + 2px breathing room.'],['STATUS','Never rely on color; pair dot with label.'],['MOTION','120ms press; 180ms panels; blink respects setting.'],['DATA','Monospace all times, durations and chart values.']];
  rules.forEach((r,i)=>{const yy=740+i*34; z+=badge(674,yy-12,r[0],i===2?C.success:C.gold)+text(774,yy,r[1],12,C.ink,550);});
  return svg(w,h,'Pixel Deck Foundations',z);
}

function componentsBoard() {
  const w=1280,h=960;
  let z=background(w,h,'PIXEL DECK COMPONENTS','REUSABLE UI SPECIMENS');
  z+=frame(24,108,602,266,{fill:C.surface,n:9});
  z+=sectionHeader(48,138,'Actions','Default, primary, danger and disabled.');
  z+=button(48,190,160,52,'START',{variant:'primary',icon:'play'});
  z+=button(222,190,160,52,'RESET',{icon:'reset'});
  z+=button(396,190,160,52,'DELETE',{variant:'danger',icon:'close'});
  z+=button(48,266,160,52,'DISABLED',{disabled:true});
  z+=button(222,266,160,52,'FLOAT',{variant:'ghost',icon:'float'});
  z+=button(396,266,160,52,'REPORTS',{icon:'chart'});

  z+=frame(650,108,606,266,{fill:C.surface,n:9});
  z+=sectionHeader(674,138,'Inputs','44px fields with persistent labels.');
  z+=field(674,186,258,'THEME','Night Deck');
  z+=field(946,186,266,'POSITION','Top right');
  z+=toggle(674,294,true,'Show REC dot','Visible during capture');
  z+=toggle(946,294,false,'Click-through','Mouse events pass through');

  z+=frame(24,400,1232,226,{fill:C.surface,n:9});
  z+=sectionHeader(48,430,'Navigation + Status','Mode tabs, badges and progress patterns.');
  z+=tab(48,484,150,'STOPWATCH',true)+tab(204,484,120,'CLOCK')+tab(330,484,146,'COUNTDOWN')+tab(482,484,138,'TIMECODE');
  z+=badge(672,494,'RUNNING',C.success)+badge(766,494,'REC LIVE',C.hp)+badge(862,494,'PAUSED',C.gold)+badge(950,494,'LOCKED',C.primary);
  z+=label(48,566,'SESSION PROGRESS');
  z+=rect(198,556,420,20,C.inset,C.borderSoft,1,2)+rect(198,556,292,20,C.gold)+rect(198,556,292,20,'url(#stripes)');
  z+=mono(632,566,'69%',12,C.gold,750);

  z+=frame(24,652,602,276,{fill:C.surface,n:9});
  z+=sectionHeader(48,682,'Timer Card','Canonical numeric display.');
  z+=rect(48,740,554,132,C.inset,C.border,2,5)+rect(56,748,538,116,'url(#stripes)',C.borderSoft,1,3);
  z+=badge(70,758,'RUNNING',C.success)+badge(486,758,'REC',C.hp);
  z+=mono(325,820,'00:14:27.6',43,C.ink,800,'middle');
  z+=text(325,854,'DEEP WORK SPRINT',11,C.muted,650,'middle');

  z+=frame(650,652,606,276,{fill:C.surface,n:9});
  z+=sectionHeader(674,682,'Data Marks','Bars, swatches and session row.');
  z+=text(674,750,'Stopwatch Overlay',13,C.ink,600)+rect(826,738,322,22,C.inset,C.borderSoft,1,2)+rect(826,738,244,22,C.primary)+rect(826,738,244,22,'url(#stripes)');
  z+=mono(1228,750,'13h 14m',11,C.muted,650,'end');
  z+=line(674,786,1230,786,C.borderSoft,1);
  z+=mono(674,818,'16:20',11,C.muted,650)+text(746,818,'Stopwatch Overlay',12,C.ink,600)+mono(940,818,'01:38:22',11,C.ink,650)+badge(1050,806,'SAVED',C.success);
  z+=line(674,842,1230,842,C.borderSoft,1);
  z+=mono(674,874,'14:03',11,C.muted,650)+text(746,874,'Client Portal',12,C.ink,600)+mono(940,874,'00:52:14',11,C.ink,650)+badge(1050,862,'COUNTDOWN',C.secondary);
  return svg(w,h,'Pixel Deck Components',z);
}

const files = {
  'pixel-deck-foundations.svg': foundationsBoard(),
  'pixel-deck-components.svg': componentsBoard(),
  'pixel-deck-settings.svg': settingsBoard(),
  'pixel-deck-dashboard.svg': dashboardBoard(),
  'pixel-deck-floating-clock.svg': floatingBoard(),
};

for (const [name, content] of Object.entries(files)) writeFileSync(join(outDir, name), content, 'utf8');

const cards = [
  ['FOUNDATIONS','pixel-deck-foundations.svg','1280 × 960'],
  ['COMPONENTS','pixel-deck-components.svg','1280 × 960'],
  ['CONTROLLER + SETTINGS','pixel-deck-settings.svg','1180 × 900'],
  ['REPORTS DASHBOARD','pixel-deck-dashboard.svg','1280 × 900'],
  ['FLOATING CLOCK','pixel-deck-floating-clock.svg','1040 × 600'],
];
const preview = `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Stopwatch Pixel Deck — Figma Boards</title><style>
  :root{color-scheme:dark;background:#071014;color:#fff8e8;font-family:Inter,Segoe UI,sans-serif}*{box-sizing:border-box}body{margin:0;background:repeating-linear-gradient(135deg,#071014 0 16px,#0b1f28 16px 32px);padding:32px}.mast{max-width:1380px;margin:0 auto 32px;padding:26px 30px;background:#3c2419;border:2px solid #d7a24b;box-shadow:8px 8px 0 #071014}.mast h1{margin:0;font:800 30px/1.1 'Cascadia Mono',monospace;color:#ffd46c;letter-spacing:1px}.mast p{margin:10px 0 0;color:#d2ad69}.grid{max-width:1380px;margin:auto;display:grid;gap:32px}.board{padding:16px;background:#112c3e;border:2px solid #796146;box-shadow:8px 8px 0 #071014}.label{display:flex;justify-content:space-between;align-items:center;margin:0 0 12px;font:700 12px/1 'Cascadia Mono',monospace;color:#ffd46c;letter-spacing:1px}.label span{color:#c7c3c3}.board img{display:block;width:100%;height:auto;background:#112c3e}.note{max-width:1380px;margin:32px auto 0;color:#c7c3c3;font-size:13px}.note code{color:#70d6a2}</style></head><body><header class="mast"><h1>STOPWATCH OVERLAY / PIXEL DECK</h1><p>Editable Figma-ready vector boards · Night Deck default · Day Deck tokens included</p></header><main class="grid">${cards.map(c=>`<section class="board"><div class="label">${c[0]} <span>${c[2]}</span></div><img src="${c[1]}" alt="${c[0]} board"></section>`).join('')}</main><p class="note">Generated from <code>generate-pixel-deck.mjs</code>. Each SVG uses only native vector primitives and editable text.</p></body></html>`;
writeFileSync(join(outDir,'preview.html'),preview,'utf8');
console.log(`Generated ${Object.keys(files).length} SVG boards and preview.html in ${outDir}`);
