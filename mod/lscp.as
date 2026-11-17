function lscpSendSteamMessage(obj)
{
   _root.lscpOriginalSendSteamMessage(obj);
   lscpSteamSend(obj);
}
function lscpSteamConnect()
{
   var s = new XMLSocket();
   _root.lscpSocket = s;
   _root.lscpSocketUp = false;
   _root.lscpSocketQueue = [];
   s.onConnect = function(ok)
   {
      _root.lscpSocketUp = ok;
      if(ok)
      {
         while(_root.lscpSocketQueue.length > 0)
         {
            _root.lscpSocket.send(_root.lscpSocketQueue.shift());
         }
      }
   };
   s.onData = function(src)
   {
      if(src.substr(0,3) == "OK ")
      {
         _root.getSteamConfirm(src.substr(3));
      }
   };
   s.onClose = function()
   {
      _root.lscpSocketUp = false;
   };
   s.connect("127.0.0.1",47615);
}
function lscpSteamSend(ach)
{
   if(_root.lscpLauncher != "1")
   {
      return undefined;
   }
   if(_root.lscpSocketUp)
   {
      _root.lscpSocket.send(ach);
      return undefined;
   }
   var i = 0;
   while(i < _root.lscpSocketQueue.length)
   {
      if(_root.lscpSocketQueue[i] == ach)
      {
         return undefined;
      }
      i++;
   }
   _root.lscpSocketQueue.push(ach);
}
function lscpFallbackStart()
{
   if(_root.gameStarted == false)
   {
      _root.sayHello({});
   }
}
function drawBoundsGlow()
{
   var w = _root.world;
   var b = w.bounds.getBounds(w);
   var g = w.createEmptyMovieClip("boundsGlow",_root.depth_world_boundsGlow);
   var s = 55;
   var bw = b.xMax - b.xMin;
   var bh = b.yMax - b.yMin;
   drawGlowStrip(g,b.xMin - s,b.yMin,s,bh,0,[0,32]);
   drawGlowStrip(g,b.xMax,b.yMin,s,bh,0,[32,0]);
   drawGlowStrip(g,b.xMin,b.yMin - s,bw,s,1.5707963267948966,[0,32]);
   drawGlowStrip(g,b.xMin,b.yMax,bw,s,1.5707963267948966,[32,0]);
}
function drawGlowStrip(g, x, y, w, h, r, alphas)
{
   g.beginGradientFill("linear",[0,0],alphas,[0,255],{matrixType:"box",x:x,y:y,w:w,h:h,r:r});
   g.moveTo(x,y);
   g.lineTo(x + w,y);
   g.lineTo(x + w,y + h);
   g.lineTo(x,y + h);
   g.lineTo(x,y);
   g.endFill();
}
function lscpLen(a)
{
   if(a == undefined)
   {
      return 0;
   }
   return a.length;
}
function lscpReport(fps, worst)
{
   var counts = {};
   var total = 0;
   for(var n in _root.world)
   {
      if(typeof _root.world[n] == "movieclip")
      {
         total++;
         var p = n;
         var cc = p.charCodeAt(p.length - 1);
         while(p.length > 1 && cc >= 48 && cc <= 57)
         {
            p = p.substring(0,p.length - 1);
            cc = p.charCodeAt(p.length - 1);
         }
         if(counts[p] == undefined)
         {
            counts[p] = 0;
         }
         counts[p]++;
      }
   }
   var list = [];
   for(var c in counts)
   {
      list.push({n:c,c:counts[c]});
   }
   list.sortOn("c",Array.NUMERIC | Array.DESCENDING);
   var s = "FPS " + Math.round(fps) + "   worst frame " + worst + " ms\n";
   s += "zombies " + lscpLen(_root.enemiesOnScreen) + "   on screen " + lscpLen(_root.allOnScreen) + "   waiting " + lscpLen(_root.holdingZombies) + "\n";
   s += "corpses " + lscpLen(_root.allDeadBodies) + "\n";
   s += "fade body " + _root.settingBodyFade + "   decal " + _root.settingDecalFade + "\n";
   s += "world clips " + total;
   var i = 0;
   while(i < list.length && i < 8)
   {
      s += "\n   " + list[i].n + " " + list[i].c;
      i++;
   }
   return s;
}
function lscpTick()
{
   var now = getTimer();
   var dt = now - this.prev;
   this.prev = now;
   if(dt > this.worst)
   {
      this.worst = dt;
   }
   this.frames++;
   if(now - this.last >= 1000)
   {
      if(this._visible)
      {
         this.txt.text = lscpReport(this.frames * 1000 / (now - this.last),this.worst);
      }
      this.frames = 0;
      this.worst = 0;
      this.last = now;
   }
}
function lscpCreateOverlay()
{
   var o = _root.createEmptyMovieClip("lscpOverlay",1048000);
   o.createTextField("txt",1,6,60,10,10);
   var t = o.txt;
   t.autoSize = "left";
   t.multiline = true;
   t.selectable = false;
   t.background = true;
   t.backgroundColor = 0;
   t.textColor = 16777215;
   var f = new TextFormat();
   f.font = "_typewriter";
   f.size = 11;
   t.setNewTextFormat(f);
   t.text = "waiting for stats";
   o._alpha = 85;
   o._visible = false;
   o.frames = 0;
   o.worst = 0;
   o.last = getTimer();
   o.prev = o.last;
   o.onEnterFrame = lscpTick;
   var keys = {};
   keys.onKeyDown = function()
   {
      if(Key.getCode() == 120)
      {
         _root.lscpOverlay._visible = !_root.lscpOverlay._visible;
      }
      if(Key.getCode() == 119 && _root.lscpTest == "1")
      {
         _root.getMedal("ACH_14");
      }
   };
   Key.addListener(keys);
}
function lscpInstall()
{
   lscpCreateOverlay();
   if(_root.lscpLauncher == "1")
   {
      lscpSteamConnect();
   }
   setTimeout(lscpFallbackStart,1500);
}
if(_root.sendSteamMessage != lscpSendSteamMessage)
{
   _root.lscpOriginalSendSteamMessage = _root.sendSteamMessage;
   _root.sendSteamMessage = lscpSendSteamMessage;
}
if(_root.lscpInstalled != true)
{
   _root.lscpInstalled = true;
   lscpInstall();
}
