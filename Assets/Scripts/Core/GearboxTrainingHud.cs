using UnityEngine;

namespace GearboxDemo
{
    public sealed partial class GearboxTrainingClient
    {
        private static readonly Color Navy = new Color(.035f,.105f,.17f);
        private static readonly Color Teal = new Color(.0f,.53f,.57f);
        private static readonly Color Ink = new Color(.06f,.16f,.26f);
        private static readonly Color MutedInk = new Color(.38f,.46f,.56f);
        private Texture2D rounded, disc;
        private GUIStyle hudTitle, hudText, hudSmall, hudButton, hudWhiteButton;

        private void EnsureHud()
        {
            if(rounded!=null)return;
            rounded=ShapeTexture(false);disc=ShapeTexture(true);
            hudTitle=TextStyle(19,Color.white,true);
            hudText=TextStyle(14,Ink);
            hudSmall=TextStyle(11,MutedInk);
            hudButton=TextStyle(13,Ink,true);hudButton.alignment=TextAnchor.MiddleCenter;
            hudWhiteButton=TextStyle(13,Color.white,true);hudWhiteButton.alignment=TextAnchor.MiddleCenter;
        }
        private static GUIStyle TextStyle(int size,Color color,bool bold=false)
        {
            var style=new GUIStyle(GUI.skin.label){fontSize=size,fontStyle=bold?FontStyle.Bold:FontStyle.Normal,clipping=TextClipping.Clip,wordWrap=false};
            style.normal.textColor=color;return style;
        }
        private static Texture2D ShapeTexture(bool circle)
        {
            const int n=64;
            var texture=new Texture2D(n,n,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[n*n];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++) {
                float distance;
                if(circle)distance=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(32,32))-31;
                else {
                    float dx=Mathf.Max(Mathf.Abs(x+.5f-32)-21,0),dy=Mathf.Max(Mathf.Abs(y+.5f-32)-21,0);
                    distance=Mathf.Sqrt(dx*dx+dy*dy)-10;
                }
                pixels[y*n+x]=new Color(1,1,1,Mathf.Clamp01(.5f-distance));
            }
            texture.SetPixels(pixels);texture.Apply();return texture;
        }
        private void Box(Rect r,Color color,bool circle=false)
        {
            GUI.color=color;
            if(circle)GUI.DrawTexture(r,disc);
            else {
                GUI.color=Color.white;
                GUI.DrawTexture(r,Texture2D.whiteTexture,ScaleMode.StretchToFill,true,0,color,0,Mathf.Min(10,r.width*.5f,r.height*.5f));
            }
            GUI.color=Color.white;
        }
        private static void Solid(Rect r,Color color){GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=Color.white;}
        private bool HudButton(Rect r,string text,bool dark=false,bool danger=false)
        {
            bool hover=r.Contains(Event.current.mousePosition);
            var fill=danger?new Color(.86f,.08f,.13f):dark?new Color(.075f,.16f,.24f):new Color(.94f,.96f,.99f);
            Box(r,hover?Color.Lerp(fill,danger?Color.white:Teal,.14f):fill);
            GUI.Label(r,text,dark||danger?hudWhiteButton:hudButton);
            return GUI.Button(r,GUIContent.none,GUIStyle.none);
        }
        private void ToggleMic()
        {
            if(!connected){StartCoroutine(Connect());return;}
            micDesired=!micDesired;
            StartCoroutine(Http("/voice/microphone",JsonUtility.ToJson(new Mic{enabled=micDesired&&!thinking}),null,text=>error=text));
        }
        private void OnGUI()
        {
            if(game==null||!game.Ready)return;
            EnsureHud();
            var previousMatrix=GUI.matrix;
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            GUI.matrix=Matrix4x4.Scale(new Vector3(scale,scale,1));
            float w=Screen.width/scale,h=Screen.height/scale;
            Solid(new Rect(0,0,w,48),Navy);
            // Small mechanical badge, with the viewport taking the full remaining area.
            Box(new Rect(18,10,28,28),new Color(.43f,.57f,.65f),true);
            Box(new Rect(26,18,12,12),Navy,true);
            GUI.Label(new Rect(58,12,365,28),"GEARBOX ASSEMBLY TRAINER",hudTitle);
            string step=session?.current_step?.title??"Voice training";
            var whiteText=TextStyle(14,Color.white);
            GUI.Label(new Rect(450,15,w-660,24),game.Completed==13?"Assembly complete":"Step "+(game.Completed+1)+" of 13  ·  "+step,whiteText);
            string status=game.Fault!=null?"Reset required":!connected?"Not connected":game.Paused?"Paused":game.Running?"Demonstrating":"Awaiting readiness";
            Box(new Rect(w-193,9,176,30),Teal);
            var centered=TextStyle(12,Color.white);centered.alignment=TextAnchor.MiddleCenter;
            GUI.Label(new Rect(w-193,9,176,30),status,centered);

            // Compact playback dock; progression is a voice command, not a Next button.
            Box(new Rect(16,h-100,438,84),new Color(.04f,.11f,.18f,.94f));
            GUI.Label(new Rect(29,h-94,128,22),game.Completed+" / 13 completed",TextStyle(12,Color.white,true));
            Solid(new Rect(161,h-82,273,3),new Color(.26f,.35f,.43f));
            for(int i=0;i<13;i++)Box(new Rect(158+i*22,h-86,10,10),i<game.Completed?new Color(.16f,.81f,.77f):new Color(.34f,.43f,.51f),true);
            if(HudButton(new Rect(25,h-63,95,36),game.Paused?"Resume":"Pause",true)){if(game.Paused)Resume();else game.Pause();}
            if(HudButton(new Rect(127,h-63,95,36),"Camera",true))game.CycleCamera();
            if(HudButton(new Rect(229,h-63,95,36),"Reset",true))SendAction("reset");
            if(HudButton(new Rect(331,h-63,112,36),"STOP",true,true))Stop();

            float x=w-342,y=h-181;
            Box(new Rect(x+1,y+5,326,165),new Color(0,0,0,.16f));
            Box(new Rect(x,y,326,165),new Color(.985f,.99f,1));
            Box(new Rect(x+13,y+13,62,62),new Color(.77f,.92f,.94f),true);
            Box(new Rect(x+17,y+17,54,54),Teal,true);
            // Vector-like microphone glyph, independent of icon font availability.
            Box(new Rect(x+39,y+29,10,22),Color.white);
            Solid(new Rect(x+33,y+43,3,10),Color.white);Solid(new Rect(x+52,y+43,3,10),Color.white);
            Box(new Rect(x+33,y+50,22,7),Color.white);
            Solid(new Rect(x+42,y+55,3,7),Color.white);Box(new Rect(x+37,y+61,13,3),Color.white);
            if(GUI.Button(new Rect(x+13,y+13,62,62),GUIContent.none,GUIStyle.none))ToggleMic();
            GUI.Label(new Rect(x+87,y+23,65,24),micDesired?"Mic ON":"Mic OFF",TextStyle(13,Ink,true));
            Box(new Rect(x+152,y+25,33,18),micDesired?Teal:new Color(.65f,.71f,.77f));
            Box(new Rect(x+(micDesired?169:155),y+28,12,12),Color.white,true);
            if(GUI.Button(new Rect(x+84,y+19,106,32),GUIContent.none,GUIStyle.none))ToggleMic();
            string activity=thinking?"Thinking":voice.speaking?"Speaking":voice.listening?"Listening":micDesired?"Starting":"Mic off";
            Box(new Rect(x+198,y+30,7,7),micDesired||voice.speaking?Teal:new Color(.65f,.71f,.77f),true);
            GUI.Label(new Rect(x+211,y+23,78,24),activity,TextStyle(11,Ink));
            if(HudButton(new Rect(x+272,y+59,40,32),muted?"Off":"Mute")) {
                muted=!muted;if(muted)StartCoroutine(Http("/voice/silence","{}",null));
            }
            // Decorative activity indicator: not a measured microphone amplitude.
            for(int i=0;i<21;i++) {
                float bar=voice.speaking?7+Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup*4+i*.7f))*21:4+Mathf.Abs(Mathf.Sin(i*.8f))*8;
                Box(new Rect(x+83+i*5.5f,y+79-bar*.5f,3,bar),micDesired||voice.speaking?new Color(.0f,.64f,.66f):new Color(.71f,.79f,.82f));
            }
            if(HudButton(new Rect(x+205,y+59,58,32),"Repeat"))Repeat();
            if(HudButton(new Rect(x+271,y+101,41,26),"CC"))subtitles=!subtitles;
            Box(new Rect(x+14,y+112,245,37),new Color(.9f,.95f,.99f));
            GUI.Label(new Rect(x+24,y+121,229,22),connected?"Say ‘go back’ or ‘I’m ready’.":"Tap the microphone to connect.",TextStyle(12,Ink));
            if(!string.IsNullOrEmpty(error)) {
                var warning=TextStyle(10,new Color(.59f,.28f,.12f));
                GUI.Label(new Rect(x+14,y+94,252,18),error.Contains("Tutor unavailable")?"Voice tutor unavailable · connect Ollama":error.Contains("Backend unavailable")?"Backend offline · tap microphone to reconnect":"Voice needs attention · check your connection",warning);
            }
            // Short subtitle strip instead of a large transcript panel.
            if(subtitles&&!string.IsNullOrEmpty(subtitle)) {
                float subtitleWidth=Mathf.Min(480,w-820);
                if(subtitleWidth>160) {
                    var strip=new Rect(470,h-61,subtitleWidth,38);
                    Box(strip,new Color(.035f,.105f,.17f,.93f));
                    string shown=subtitle.Length>105?subtitle.Substring(0,102)+"…":subtitle;
                    var sub=TextStyle(12,Color.white);sub.wordWrap=true;
                    GUI.Label(new Rect(strip.x+13,strip.y+5,strip.width-26,29),new GUIContent("Tutor: "+shown,subtitle),sub);
                }
            }
            GUI.matrix=previousMatrix;
        }
    }
}
