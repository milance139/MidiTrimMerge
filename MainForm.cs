using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MidiTool
{
    class Clip
    {
        public string Path;
        public MidiSequence Seq;
        public bool Karaoke;        // file contains karaoke text
        public int Volume = 100;    // percent (note velocity scale)
        public Dictionary<int, int> Programs;   // forced instrument per channel (channel -> GM program); null = original
        public double Bpm;          // first tempo of the file
        public int KeyPc;           // tonic pitch class, 0 = C
        public bool KeyMinor, KeyKnown, KeyEst;   // KeyEst: guessed from the notes (no key signature in the file)
        public Clip Copy() { return (Clip)MemberwiseClone(); }
        public MidiSequence Edited; // copy of Seq with edited karaoke text (null = unchanged)
        public string[] LyricTexts; // edited text per lyric event (null entry = unchanged)
        public int LyricEnc;        // code page used for the text (0 = automatic)
        public MidiSequence Source { get { return Edited ?? Seq; } }
        public int Transpose;       // semitones
        public int Speed = 100;     // percent
        public double OverlapAfter = -1;   // overlap into the next song, seconds; -1 = use the general value
        public double Duration;     // seconds
        public double Start;        // trim start, seconds
        public double End;          // trim end, seconds; 0 = to the end
        public string Name { get { return System.IO.Path.GetFileName(Path); } }
        public double EffectiveEnd { get { return End > 0 && End < Duration ? End : Duration; } }
        public bool IsTrimmed { get { return Start > 0 || (End > 0 && End < Duration); } }
    }

    static class L
    {
        public static int Lang;   // 0 = English, 1 = Srpski

        static readonly Dictionary<string, string[]> S = new Dictionary<string, string[]>
        {
            { "title",      new[] { "MIDI Trim & Merge", "MIDI sečenje i spajanje" } },
            { "helpBtn",    new[] { "How to use", "Uputstvo" } },
            { "helpTitle",  new[] { "How to use", "Uputstvo za korišćenje" } },
            { "close",      new[] { "Close", "Zatvori" } },
            { "helpText",   new[] {
                "HOW TO USE\r\n\r\n" +
                "1. ADD FILES\r\n" +
                "   Click \"Add files...\" or drag .mid files into the window. You can add as many as you like. " +
                "To change their order, drag a file up or down in the list with the mouse (a line shows where it will land), " +
                "or use \"Move up\" / \"Move down\". \"Remove\" takes one out of the list.\r\n\r\n" +
                "2. TRIM A FILE (optional)\r\n" +
                "   Select a file in the list. Set the Start and the End time using the separate boxes for minutes (min) and seconds (sec). " +
                "The End is set to the full length of the song by default, so only change it if you want to cut off the ending. " +
                "Click \"Apply trim\". The column \"Used part\" shows which part of the file will be used. " +
                "\"Reset\" brings back the whole file. The original file is never changed.\r\n\r\n" +
                "3. MERGE\r\n" +
                "   Choose the merge mode:\r\n" +
                "   - One after another: the files play in the order of the list, each one after the previous.\r\n" +
                "   - Layered: all files play at the same time from the start (tempo is taken from the first file).\r\n" +
                "   Then click \"Merge & Save...\" and choose where to save the new .mid file.\r\n\r\n" +
                "4. OVERLAP BETWEEN SONGS (optional)\r\n" +
                "   WHAT IT DOES: normally the next song starts only after the previous one has completely finished. " +
                "With overlap, the next song starts a few seconds EARLY, so the ending of one song and the beginning of the next play at the same time. " +
                "This gives a smooth, continuous transition without a gap or an abrupt switch.\r\n" +
                "   HOW TO USE: in the bottom bar find \"Overlap between songs\" and type the number of seconds (0 to 60, steps of 0.5), or use the arrows. " +
                "Then click \"Merge & Save...\" as usual. The value 0 means no overlap, exactly like before.\r\n" +
                "   EXAMPLE: song A is 3:00 long, song B is 2:00 long, overlap is 4 seconds. Song B starts at 2:56, while the last 4 seconds of song A are still playing. " +
                "The merged song is 4:56 long instead of 5:00 - always shorter by the overlap for every joint between two songs. " +
                "The \"Length of the merged song\" label shows the new length immediately.\r\n" +
                "   GOOD TO KNOW:\r\n" +
                "   - It works only in \"One after another\" mode. In \"Layered\" mode the box is grayed out, because all songs already play together.\r\n" +
                "   - The overlap is applied to the part of the song that is actually used, so if you trimmed a song, the overlap is taken from the trimmed ending and the trimmed beginning.\r\n" +
                "   - It is applied between every two neighbouring songs in the list, so the order of the list matters.\r\n" +
                "   - If a song is shorter than the chosen overlap, the overlap for that joint is automatically reduced to the length of the shorter song.\r\n" +
                "   - If the next song has a different tempo, its tempo starts only when the overlap is over, so the ending of the previous song does not speed up or slow down.\r\n" +
                "   - Nothing fades out or in: both songs play at full volume during the overlap. If they use the same instruments (channels), " +
                "the notes simply sound together, and the next song's instrument and volume settings may also affect the ending of the previous one. " +
                "If it sounds too crowded, use a shorter overlap or trim the end of the first song a little.\r\n" +
                "   - Good starting values are 2 to 5 seconds. Save the file and listen to it; change the value and save again if needed.\r\n\r\n" +
                "5. LISTEN BEFORE YOU SAVE\r\n" +
                "   \"Play selected song\" plays the selected song (only the part you set with Start and End, with all its settings). " +
                "\"Play merged result\" plays the whole merged song exactly as it will be saved, including the overlaps. " +
                "Click the same button again (it says Stop) to stop. The sound comes from the built-in Windows synthesizer, so it can sound a little different from your own keyboard or player.\r\n\r\n" +
                "6. ADVANCED SETTINGS (button \"Advanced settings...\")\r\n" +
                "   The window stays open next to the main one and always shows the song that is selected in the list.\r\n" +
                "   - Transpose: moves the whole song up or down in semitones (12 = one octave). Drums are not changed. Useful to fit the singer's voice.\r\n" +
                "   - Speed: 100% is normal; 80% is slower, 120% is faster. The length of the song changes with it.\r\n" +
                "   - Own overlap after this song: normally one overlap value is used for all joints. Tick this box to give the joint between this song and the next one its own value. " +
                "(Ignored for the last song in the list.)\r\n" +
                "   - Auto-trim silence: sets Start to the first note and End to the last note, so the quiet beginning and ending are cut off. " +
                "You can do it for one song or for all songs; check the result in the \"Used part\" column and change it by hand if you wish.\r\n" +
                "   - Karaoke text (view / edit): shows the words of the song, one part (syllable) per row with its time (only for files with karaoke text, marked in the Karaoke column). " +
                "Double-click a row or press F2 to correct the text, or use Find / Replace to fix a letter everywhere. " +
                "If letters like č, ć, š, ž look wrong, change \"Read text as\" (Windows-1250 is Latin, Windows-1251 is Cyrillic). " +
                "Click Apply: the corrected text is saved into the merged .mid file, and the Karaoke column shows ♫*. \"Back to original\" undoes all changes. " +
                "The timing of the words never changes.\r\n" +
                "   - Hide title and credit lines (box in the plain text window and in Live lyrics): many karaoke files put copyright, phone, e-mail and web lines in front of the words. " +
                "With the box ticked (default) they are left out of what you read and sing from. Only the display changes, never the file. " +
                "The Karaoke column shows ♫ only for songs that really have words; files that contain nothing but credit lines show –.\r\n" +
                "   - Show text (plain view): next to it, shows the whole text as ordinary reading text (with your corrections), with a Copy button.\r\n" +
                "   - Save / Load playlist: saves the list of songs with all their trims and settings to a small .mtm file, and loads it again later. " +
                "The MIDI files themselves must stay where they are.\r\n" +
                "   - Remember...: when ticked, the program remembers the window position and size, merge mode, overlap and the last folders.\r\n" +
                "   Settings changed here are shown in the \"Used part\" column, for example (+2, 90%).\r\n\r\n" +
                "7. MORE TOOLS\r\n" +
                "   - Seek bar (under the list): drag it to play from any place. Stopping works like a pause: Play continues from the same place. " +
                "While a single song is playing, \"Start here\" / \"End here\" put the playing position into the Start / End boxes; click \"Apply trim\" to use it.\r\n" +
                "   - Live lyrics: opens a big window where the words are highlighted while the song plays (works for the selected song and for the merged result).\r\n" +
                "   - BPM and Key columns: the tempo and key of each song, including your Speed and Transpose settings. A \"?\" after the key means it was guessed from the notes. " +
                "Handy for choosing songs that fit one after another.\r\n" +
                "   - Undo / Redo (or Ctrl+Z / Ctrl+Y): takes back the last changes to the list and the settings, also the ones made in Advanced settings.\r\n" +
                "   - Smooth fade during overlap: the ending song fades out while the next one fades in. For this the next song may be moved to other MIDI channels, so both can be controlled separately. " +
                "Untick it to have both play at full volume.\r\n" +
                "   - In Advanced settings: Volume per song, Instrument change per channel (e.g. piano to accordion), \"Match loudness of all songs\" and the output device (\"Play through\") for listening on another synthesizer or a connected keyboard. " +
                "The instrument change and the volume are written into the merged file as well.\r\n\r\n" +
                "8. YOUR WORK IS KEPT SAFE\r\n" +
                "   - The list (songs, trims and all settings) is saved automatically a moment after every change, and comes back when you start the program again. " +
                "You can switch this off in Advanced settings (\"Reopen the last list...\"). If a song of the list was moved or deleted in the meantime, the program tells you which one and keeps a copy of the full old list.\r\n" +
                "   - If the program meets an unexpected error, it shows a message instead of just closing, saves the list, and lets you continue. Details are written to the file MidiTrimMerge.log in the program's data folder (press Win+R and type %appdata%).\r\n\r\n" +
                "SAVE ONE FILE\r\n" +
                "   To save only one trimmed file, select it and click \"Save this file trimmed...\".\r\n\r\n" +
                "LANGUAGE\r\n" +
                "   Use the language box in the bottom right corner to switch between English and Srpski.",

                "UPUTSTVO ZA KORIŠĆENJE\r\n\r\n" +
                "1. DODAVANJE FAJLOVA\r\n" +
                "   Kliknite \"Dodaj fajlove...\" ili prevucite .mid fajlove u prozor. Možete dodati koliko god želite. " +
                "Redosled menjate prevlačenjem fajla gore ili dole u listi mišem (linija pokazuje gde će sleteti), " +
                "ili dugmadima \"Gore\" / \"Dole\". \"Ukloni\" izbacuje fajl iz liste.\r\n\r\n" +
                "2. SEČENJE FAJLA (nije obavezno)\r\n" +
                "   Izaberite fajl u listi. Podesite Početak i Kraj pomoću odvojenih polja za minute (min) i sekunde (sek). " +
                "Kraj je podrazumevano postavljen na punu dužinu pesme, pa ga menjajte samo ako želite da odsečete završetak. " +
                "Kliknite \"Primeni\". Kolona \"Korišćeni deo\" prikazuje koji deo fajla će se koristiti. " +
                "\"Poništi\" vraća ceo fajl. Originalni fajl se nikada ne menja.\r\n\r\n" +
                "3. SPAJANJE\r\n" +
                "   Izaberite način spajanja:\r\n" +
                "   - Jedan za drugim: fajlovi se sviraju redom iz liste, svaki posle prethodnog.\r\n" +
                "   - Preklopljeno: svi fajlovi sviraju istovremeno od početka (tempo se uzima iz prvog fajla).\r\n" +
                "   Zatim kliknite \"Spoji & Sačuvaj...\" i izaberite gde da se sačuva novi .mid fajl.\r\n\r\n" +
                "4. PREKLAPANJE PESAMA (nije obavezno)\r\n" +
                "   ŠTA RADI: obično sledeća pesma počinje tek kada se prethodna potpuno završi. " +
                "Sa preklapanjem, sledeća pesma počinje nekoliko sekundi RANIJE, pa kraj jedne pesme i početak sledeće sviraju istovremeno. " +
                "Tako se dobija glatki, neprekidni prelaz bez pauze i bez naglog prekida.\r\n" +
                "   KAKO SE KORISTI: u donjoj traci pronađite \"Preklapanje pesama\" i upišite broj sekundi (0 do 60, korak 0,5), ili koristite strelice. " +
                "Zatim kliknite \"Spoji & Sačuvaj...\" kao i obično. Vrednost 0 znači bez preklapanja, potpuno isto kao ranije.\r\n" +
                "   PRIMER: pesma A traje 3:00, pesma B traje 2:00, preklapanje je 4 sekunde. Pesma B počinje u 2:56, dok poslednje 4 sekunde pesme A još sviraju. " +
                "Spojena pesma traje 4:56 umesto 5:00 - uvek kraće za vrednost preklapanja za svaki spoj dve pesme. " +
                "Natpis \"Dužina spojene pesme\" odmah prikazuje novu dužinu.\r\n" +
                "   DOBRO JE ZNATI:\r\n" +
                "   - Radi samo u načinu \"Jedan za drugim\". U načinu \"Preklopljeno\" polje je zasivljeno, jer sve pesme već sviraju zajedno.\r\n" +
                "   - Preklapanje se primenjuje na deo pesme koji se zaista koristi, pa ako ste pesmu isekli, uzima se isečeni kraj i isečeni početak.\r\n" +
                "   - Primenjuje se između svake dve susedne pesme u listi, pa je redosled u listi važan.\r\n" +
                "   - Ako je pesma kraća od izabranog preklapanja, preklapanje za taj spoj se automatski smanjuje na dužinu kraće pesme.\r\n" +
                "   - Ako sledeća pesma ima drugačiji tempo, njen tempo počinje tek kada se preklapanje završi, da se kraj prethodne pesme ne bi ubrzao ili usporio.\r\n" +
                "   - Ništa se ne utišava niti pojačava postepeno: obe pesme sviraju punom jačinom tokom preklapanja. Ako koriste iste instrumente (kanale), " +
                "note jednostavno sviraju zajedno, a podešavanja instrumenta i jačine sledeće pesme mogu uticati i na kraj prethodne. " +
                "Ako zvuči prepuno, koristite kraće preklapanje ili malo isecite kraj prve pesme.\r\n" +
                "   - Dobre početne vrednosti su 2 do 5 sekundi. Sačuvajte fajl i poslušajte ga; po potrebi promenite vrednost i sačuvajte ponovo.\r\n\r\n" +
                "5. POSLUŠAJTE PRE ČUVANJA\r\n" +
                "   \"Slušaj izabranu pesmu\" pušta izabranu pesmu (samo deo koji ste podesili sa Početak i Kraj, sa svim njenim podešavanjima). " +
                "\"Slušaj spojenu pesmu\" pušta celu spojenu pesmu tačno onako kako će biti sačuvana, zajedno sa preklapanjima. " +
                "Kliknite isto dugme ponovo (piše Zaustavi) da zaustavite. Zvuk dolazi iz ugrađenog Windows sintisajzera, pa može zvučati malo drugačije nego na vašoj klavijaturi ili plejeru.\r\n\r\n" +
                "6. NAPREDNA PODEŠAVANJA (dugme \"Napredna podešavanja...\")\r\n" +
                "   Prozor ostaje otvoren pored glavnog i uvek prikazuje pesmu koja je izabrana u listi.\r\n" +
                "   - Transponovanje: pomera celu pesmu naviše ili naniže u polutonovima (12 = jedna oktava). Bubnjevi se ne menjaju. Korisno da se pesma prilagodi glasu pevača.\r\n" +
                "   - Brzina: 100% je normalno; 80% je sporije, 120% je brže. Dužina pesme se menja u skladu s tim.\r\n" +
                "   - Posebno preklapanje posle ove pesme: obično se za sve spojeve koristi jedna vrednost preklapanja. Štiklirajte ovo polje da spoj između ove i sledeće pesme dobije svoju vrednost. " +
                "(Ne važi za poslednju pesmu u listi.)\r\n" +
                "   - Ukloni tišinu: postavlja Početak na prvu notu, a Kraj na poslednju notu, pa se tihi početak i kraj odsecaju. " +
                "Može za jednu pesmu ili za sve pesme; proverite rezultat u koloni \"Korišćeni deo\" i po želji ga ručno promenite.\r\n" +
                "   - Karaoke tekst (pregled / izmena): prikazuje reči pesme, jedan deo (slog) po redu sa njegovim vremenom (samo za fajlove sa karaoke tekstom, označene u koloni Karaoke). " +
                "Dvaput kliknite red ili pritisnite F2 da ispravite tekst, ili koristite Traži / Zameni da ispravite slovo svuda. " +
                "Ako slova poput č, ć, š, ž izgledaju pogrešno, promenite \"Čitaj tekst kao\" (Windows-1250 je latinica, Windows-1251 je ćirilica). " +
                "Kliknite Primeni: ispravljen tekst se upisuje u spojeni .mid fajl, a kolona Karaoke prikazuje ♫*. \"Vrati original\" poništava sve izmene. " +
                "Vreme reči se nikada ne menja.\r\n" +
                "   - Sakrij naslov i redove sa autorskim pravima (polje u prozoru sa običnim tekstom i u Tekstu uživo): mnogi karaoke fajlovi ispred reči stavljaju redove sa autorskim pravima, telefonom, e-poštom i web adresom. " +
                "Kada je polje štiklirano (podrazumevano) oni se izostavljaju iz teksta koji čitate i pevate. Menja se samo prikaz, nikada fajl. " +
                "Kolona Karaoke prikazuje ♫ samo za pesme koje zaista imaju reči; fajlovi koji sadrže samo redove sa autorskim pravima prikazuju –.\r\n" +
                "   - Prikaži tekst (običan prikaz): pored njega, prikazuje ceo tekst kao običan tekst za čitanje (sa vašim ispravkama), sa dugmetom Kopiraj.\r\n" +
                "   - Sačuvaj / Učitaj listu: čuva spisak pesama sa svim sečenjima i podešavanjima u mali .mtm fajl, i kasnije ga ponovo učitava. " +
                "MIDI fajlovi moraju ostati tamo gde jesu.\r\n" +
                "   - Zapamti...: kada je štiklirano, program pamti položaj i veličinu prozora, način spajanja, preklapanje i poslednje foldere.\r\n" +
                "   Podešavanja promenjena ovde vide se u koloni \"Korišćeni deo\", na primer (+2, 90%).\r\n\r\n" +
                "7. JOŠ ALATA\r\n" +
                "   - Traka za premotavanje (ispod liste): prevucite je da pesma krene sa bilo kog mesta. Zaustavljanje radi kao pauza: Slušaj nastavlja sa istog mesta. " +
                "Dok svira jedna pesma, \"Početak ovde\" / \"Kraj ovde\" upisuju trenutni položaj u polja Početak / Kraj; kliknite \"Primeni\" da se koristi.\r\n" +
                "   - Tekst uživo: otvara veliki prozor u kome se reči označavaju dok pesma svira (za izabranu pesmu i za spojenu pesmu).\r\n" +
                "   - Kolone BPM i Ton: tempo i ton svake pesme, uključujući vaša podešavanja Brzine i Transponovanja. \"?\" posle tona znači da je pogođen iz nota. " +
                "Korisno za biranje pesama koje se dobro nadovezuju.\r\n" +
                "   - Opozovi / Ponovi (ili Ctrl+Z / Ctrl+Y): vraća poslednje izmene liste i podešavanja, i one iz Naprednih podešavanja.\r\n" +
                "   - Postepeno stišavanje pri preklapanju: pesma koja se završava stišava se dok se sledeća pojačava. Zbog toga sledeća pesma može biti premeštena na druge MIDI kanale, da se obe mogu odvojeno kontrolisati. " +
                "Isključite ako želite da obe sviraju punom jačinom.\r\n" +
                "   - U Naprednim podešavanjima: Jačina po pesmi, promena instrumenta po kanalu (npr. klavir u harmoniku), \"Izjednači jačinu svih pesama\" i izlazni uređaj (\"Puštaj preko\") za slušanje preko drugog sintisajzera ili priključene klavijature. " +
                "Promena instrumenta i jačine upisuje se i u spojeni fajl.\r\n\r\n" +
                "8. VAŠ RAD JE SIGURAN\r\n" +
                "   - Lista (pesme, sečenja i sva podešavanja) čuva se automatski trenutak posle svake izmene i vraća se kada ponovo pokrenete program. " +
                "To možete isključiti u Naprednim podešavanjima (\"Otvori poslednju listu...\"). Ako je neka pesma iz liste u međuvremenu premeštena ili obrisana, program vam kaže koja i čuva kopiju cele stare liste.\r\n" +
                "   - Ako program naiđe na neočekivanu grešku, prikazuje poruku umesto da se samo zatvori, čuva listu i omogućava vam da nastavite. Detalji se upisuju u fajl MidiTrimMerge.log u fascikli sa podacima programa (pritisnite Win+R i upišite %appdata%).\r\n\r\n" +
                "ČUVANJE JEDNOG FAJLA\r\n" +
                "   Da sačuvate samo jedan isečen fajl, izaberite ga i kliknite \"Sačuvaj ovaj fajl isečen...\".\r\n\r\n" +
                "JEZIK\r\n" +
                "   Polje za jezik u donjem desnom uglu menja jezik između English i Srpski." } },
            { "language",   new[] { "Language:", "Jezik:" } },
            { "debtBtn",    new[] { "Debt", "Dug" } },
            { "debtTitle",  new[] { "Debt to Milan", "Dug prema Milanu" } },
            { "debtLine",   new[] { "Debt to Milan:", "Dug zetu Milanu:" } },
            { "debtNote",   new[] { "1 KM for every saved merged song (saved so far: {0})", "1 KM za svaku sačuvanu spojenu pesmu (do sada sačuvano: {0})" } },
            { "total",      new[] { "Length of the merged song:", "Dužina spojene pesme:" } },
            { "smaller",    new[] { "Smaller text", "Manji tekst" } },
            { "larger",     new[] { "Larger text", "Veći tekst" } },
            { "colFile",    new[] { "File", "Fajl" } },
            { "colLen",     new[] { "Length", "Dužina" } },
            { "colUsed",    new[] { "Used part", "Korišćeni deo" } },
            { "colKar",     new[] { "Karaoke", "Karaoke" } },
            { "whole",      new[] { "whole file", "ceo fajl" } },
            { "add",        new[] { "Add files...", "Dodaj fajlove..." } },
            { "remove",     new[] { "Remove", "Ukloni" } },
            { "up",         new[] { "Move up", "Gore" } },
            { "down",       new[] { "Move down", "Dole" } },
            { "trimBox",    new[] { "Trim selected file", "Seci izabrani fajl" } },
            { "start",      new[] { "Start", "Početak" } },
            { "end",        new[] { "End", "Kraj" } },
            { "unitMin",    new[] { "min", "min" } },
            { "unitSec",    new[] { "sec", "sek" } },
            { "apply",      new[] { "Apply trim", "Primeni" } },
            { "reset",      new[] { "Reset", "Poništi" } },
            { "saveOne",    new[] { "Save this file trimmed...", "Sačuvaj ovaj fajl isečen..." } },
            { "mode",       new[] { "Merge mode:", "Način spajanja:" } },
            { "modeSeq",    new[] { "One after another (in list order)", "Jedan za drugim (redom iz liste)" } },
            { "modeLayer",  new[] { "Layered (all together, tempo of first file)", "Preklopljeno (sve istovremeno, tempo prvog fajla)" } },
            { "overlap",    new[] { "Overlap between songs:", "Preklapanje pesama:" } },
            { "overlapSec", new[] { "sec", "sek" } },
            { "overlapTip", new[] { "The end of each song plays together with the start of the next one for this many seconds (only for \"One after another\").", "Kraj svake pesme svira zajedno sa početkom sledeće ovoliko sekundi (samo za \"Jedan za drugim\")." } },
            { "hideCredits", new[] { "Hide title and credit lines", "Sakrij naslov i redove sa autorskim pravima" } },
            { "hideCreditsTip", new[] { "Leaves out the copyright, phone, e-mail and web lines that many karaoke files put in front of the words. Only the display changes, never the file.", "Izostavlja redove sa autorskim pravima, telefonom, e-poštom i web adresom koje mnogi karaoke fajlovi stavljaju ispred reči. Menja se samo prikaz, nikada fajl." } },
            { "onlyCredits", new[] { "(Only credit lines were found. Untick \"Hide title and credit lines\" to see them.)", "(Pronađeni su samo redovi sa autorskim pravima. Isključite \"Sakrij naslov i redove sa autorskim pravima\" da ih vidite.)" } },
            { "restored",   new[] { "Your last list was restored ({0} song(s)).", "Vraćena je vaša poslednja lista (pesama: {0})." } },
            { "restoreMissing", new[] { "Some songs of your last list were not found any more:\n{0}\n\nThe full previous list was kept as {1} in the program's data folder.", "Neke pesme iz vaše poslednje liste više nisu pronađene:\n{0}\n\nPuna prethodna lista sačuvana je kao {1} u fascikli sa podacima programa." } },
            { "restoreOpt", new[] { "Reopen the last list when the program starts", "Otvori poslednju listu kada se program pokrene" } },
            { "crashTitle", new[] { "Unexpected error", "Neočekivana greška" } },
            { "crashText",  new[] { "Something went wrong:\n{0}\n\nYour list was saved automatically and comes back the next time the program starts.", "Nešto je pošlo naopako:\n{0}\n\nVaša lista je automatski sačuvana i vratiće se pri sledećem pokretanju programa." } },
            { "crashAsk",   new[] { "Continue working? (No closes the program.)", "Nastaviti sa radom? (Ne zatvara program.)" } },
            { "crashFatal", new[] { "The program has to close now.", "Program se sada mora zatvoriti." } },
            { "colBpm",     new[] { "BPM", "BPM" } },
            { "colKey",     new[] { "Key", "Ton" } },
            { "undo",       new[] { "Undo", "Opozovi" } },
            { "redo",       new[] { "Redo", "Ponovi" } },
            { "fade",       new[] { "Smooth fade during overlap", "Postepeno stišavanje pri preklapanju" } },
            { "fadeTip",    new[] { "The ending song fades out while the next one fades in. The next song may be moved to other MIDI channels during the overlap.", "Pesma koja se završava postepeno se stišava dok se sledeća pojačava. Sledeća pesma može biti premeštena na druge MIDI kanale tokom preklapanja." } },
            { "setStart",   new[] { "Start here", "Početak ovde" } },
            { "setEnd",     new[] { "End here", "Kraj ovde" } },
            { "setStartTip",new[] { "Put the current playing position into the Start box (then click Apply trim).", "Upiši trenutni položaj u polje Početak (zatim kliknite Primeni)." } },
            { "setEndTip",  new[] { "Put the current playing position into the End box (then click Apply trim).", "Upiši trenutni položaj u polje Kraj (zatim kliknite Primeni)." } },
            { "startSet",   new[] { "Start box set to {0}. Click \"Apply trim\" to use it.", "Polje Početak postavljeno na {0}. Kliknite \"Primeni\" da se koristi." } },
            { "endSet",     new[] { "End box set to {0}. Click \"Apply trim\" to use it.", "Polje Kraj postavljeno na {0}. Kliknite \"Primeni\" da se koristi." } },
            { "live",       new[] { "Live lyrics...", "Tekst uživo..." } },
            { "liveTitle",  new[] { "Live karaoke text", "Karaoke tekst uživo" } },
            { "liveNone",   new[] { "This has no karaoke text.", "Nema karaoke teksta." } },
            { "volume",     new[] { "Volume (%):", "Jačina (%):" } },
            { "instrument", new[] { "Instrument:", "Instrument:" } },
            { "channel",    new[] { "Channel:", "Kanal:" } },
            { "instrOrig",  new[] { "(original)", "(original)" } },
            { "matchLoud",  new[] { "Match loudness of all songs", "Izjednači jačinu svih pesama" } },
            { "loudDone",   new[] { "Loudness matched for {0} song(s).", "Jačina izjednačena za pesama: {0}." } },
            { "device",     new[] { "Play through:", "Puštaj preko:" } },
            { "devDefault", new[] { "Windows default", "Windows podrazumevano" } },
            { "grpSong",    new[] { "Selected song", "Izabrana pesma" } },
            { "grpAll",     new[] { "All songs", "Sve pesme" } },
            { "grpFile",    new[] { "Playlist and program", "Lista i program" } },
            { "play",       new[] { "Play selected song", "Slušaj izabranu pesmu" } },
            { "playAll",    new[] { "Play merged result", "Slušaj spojenu pesmu" } },
            { "stop",       new[] { "Stop", "Zaustavi" } },
            { "playing",    new[] { "Playing...", "Svira..." } },
            { "playFail",   new[] { "Could not play: {0}", "Puštanje nije uspelo: {0}" } },
            { "advBtn",     new[] { "Advanced settings...", "Napredna podešavanja..." } },
            { "advTitle",   new[] { "Advanced settings", "Napredna podešavanja" } },
            { "advSong",    new[] { "Settings for the selected song:", "Podešavanja za izabranu pesmu:" } },
            { "advNone",    new[] { "Select a song in the list to change its settings.", "Izaberite pesmu u listi da biste menjali njena podešavanja." } },
            { "transpose",  new[] { "Transpose (semitones):", "Transponovanje (polutonovi):" } },
            { "speed",      new[] { "Speed (%):", "Brzina (%):" } },
            { "ovOwn",      new[] { "Own overlap after this song:", "Posebno preklapanje posle ove pesme:" } },
            { "resetSong",  new[] { "Reset this song's settings", "Poništi podešavanja ove pesme" } },
            { "autoOne",    new[] { "Auto-trim silence (this song)", "Ukloni tišinu (ova pesma)" } },
            { "autoAll",    new[] { "Auto-trim silence (all songs)", "Ukloni tišinu (sve pesme)" } },
            { "autoDone",   new[] { "Auto-trim: {0} song(s) changed.", "Automatsko sečenje: promenjeno pesama: {0}." } },
            { "lyricsBtn",  new[] { "Karaoke text (view / edit)...", "Karaoke tekst (pregled / izmena)..." } },
            { "lyricsPlain",new[] { "Show text (plain view)...", "Prikaži tekst (običan prikaz)..." } },
            { "plainTitle", new[] { "Karaoke text (plain) - {0}", "Karaoke tekst (običan) - {0}" } },
            { "copy",       new[] { "Copy", "Kopiraj" } },
            { "lyricsTitle",new[] { "Karaoke text - {0}", "Karaoke tekst - {0}" } },
            { "lyHelp",     new[] { "Double-click a row (or press F2) to change its text. A \"/\" at the start of a part means a new line, \"\\\" means a new paragraph - keep them where they are.",
                                    "Dvaput kliknite red (ili F2) da promenite tekst. \"/\" na početku dela znači novi red, a \"\\\" novi pasus - ostavite ih gde jesu." } },
            { "lyTime",     new[] { "Time", "Vreme" } },
            { "lyText",     new[] { "Text", "Tekst" } },
            { "lyEnc",      new[] { "Read text as:", "Čitaj tekst kao:" } },
            { "lyEncAuto",  new[] { "Automatic", "Automatski" } },
            { "lyFind",     new[] { "Find:", "Traži:" } },
            { "lyRepl",     new[] { "Replace with:", "Zameni sa:" } },
            { "lyReplAll",  new[] { "Replace all", "Zameni sve" } },
            { "lyReplDone", new[] { "Replaced in {0} row(s).", "Zamenjeno u redova: {0}." } },
            { "lyApply",    new[] { "Apply", "Primeni" } },
            { "lyReset",    new[] { "Back to original", "Vrati original" } },
            { "lyCancel",   new[] { "Cancel", "Otkaži" } },
            { "lyApplied",  new[] { "Karaoke text updated for {0}.", "Karaoke tekst izmenjen za {0}." } },
            { "lyEncWarn",  new[] { "Changing this reloads the text from the original file; your unsaved edits in this window are lost. Continue?", "Promena ovoga ponovo učitava tekst iz originalnog fajla; izmene u ovom prozoru koje niste primenili biće izgubljene. Nastaviti?" } },
            { "noLyrics",   new[] { "This song has no karaoke text.", "Ova pesma nema karaoke tekst." } },
            { "savePl",     new[] { "Save playlist...", "Sačuvaj listu..." } },
            { "loadPl",     new[] { "Load playlist...", "Učitaj listu..." } },
            { "plFilter",   new[] { "Playlist (*.mtm)|*.mtm", "Lista pesama (*.mtm)|*.mtm" } },
            { "plSaved",    new[] { "Playlist saved: {0}", "Lista sačuvana: {0}" } },
            { "plLoaded",   new[] { "Loaded {0} song(s) from the playlist.", "Učitano pesama iz liste: {0}." } },
            { "plMissing",  new[] { "These files could not be loaded:", "Ovi fajlovi nisu mogli da se učitaju:" } },
            { "plReplace",  new[] { "Replace the current list with the playlist?", "Zameniti trenutnu listu učitanom listom?" } },
            { "plFail",     new[] { "Could not read the playlist", "Lista se ne može pročitati" } },
            { "remember",   new[] { "Remember window position, merge mode, overlap and folders", "Zapamti položaj prozora, način spajanja, preklapanje i foldere" } },
            { "merge",      new[] { "Merge && Save...", "Spoji && Sačuvaj..." } },
            { "hintEmpty",  new[] { "Add MIDI files, then select one.", "Dodajte MIDI fajlove, pa izaberite jedan." } },
            { "hintSelect", new[] { "Select a file to trim it.", "Izaberite fajl da biste ga isekli." } },
            { "statusHelp", new[] { "Add MIDI files with the button or drag them onto this window.", "Dodajte MIDI fajlove dugmetom ili ih prevucite u ovaj prozor." } },
            { "infoLen",    new[] { "Length", "Dužina" } },
            { "infoTracks", new[] { "Tracks", "Staze" } },
            { "infoNotes",  new[] { "Notes", "Note" } },
            { "added",      new[] { "Added {0} file(s). {1} in list.", "Dodato fajlova: {0}. U listi: {1}." } },
            { "loadFail",   new[] { "Some files could not be loaded", "Neki fajlovi nisu mogli da se učitaju" } },
            { "badTime",    new[] { "Enter times as seconds (e.g. 12.5) or m:ss.ff (e.g. 1:05.50).", "Unesite vreme u sekundama (npr. 12.5) ili m:ss.ff (npr. 1:05.50)." } },
            { "badTimeT",   new[] { "Invalid time", "Neispravno vreme" } },
            { "startBeyond",new[] { "Start is beyond the end of the file.", "Početak je posle kraja fajla." } },
            { "endBefore",  new[] { "End must be after start.", "Kraj mora biti posle početka." } },
            { "trimSet",    new[] { "Trim set for {0}.", "Sečenje podešeno za {0}." } },
            { "need2",      new[] { "Add at least two files to merge.", "Dodajte bar dva fajla za spajanje." } },
            { "saved",      new[] { "Saved {0}", "Sačuvano: {0}" } },
            { "merged",     new[] { "Merged {0} files into {1} (length {2})", "Spojeno fajlova: {0} u {1} (dužina {2})" } },
            { "saveFail",   new[] { "Could not save", "Čuvanje nije uspelo" } },
            { "mergeFail",  new[] { "Could not merge", "Spajanje nije uspelo" } },
            { "midiFiles",  new[] { "MIDI files (*.mid;*.midi;*.kar)|*.mid;*.midi;*.kar|All files|*.*", "MIDI fajlovi (*.mid;*.midi;*.kar)|*.mid;*.midi;*.kar|Svi fajlovi|*.*" } },
            { "midiSave",   new[] { "MIDI file (*.mid)|*.mid", "MIDI fajl (*.mid)|*.mid" } },
            { "errNotMidi", new[] { "Not a standard MIDI file.", "Nije standardni MIDI fajl." } },
            { "errSmpte",   new[] { "SMPTE time-division MIDI files are not supported.", "MIDI fajlovi sa SMPTE vremenom nisu podržani." } },
        };

        public static string T(string key) { return S[key][Lang]; }
        public static string T(string key, params object[] a) { return string.Format(S[key][Lang], a); }
    }

    public partial class MainForm : Form
    {
        readonly List<Clip> clips = new List<Clip>();
        readonly ListView list = new ListView();
        readonly Label info = new Label();
        readonly NumericUpDown startMin = new NumericUpDown();
        readonly NumericUpDown startSec = new NumericUpDown();
        readonly NumericUpDown endMin = new NumericUpDown();
        readonly NumericUpDown endSec = new NumericUpDown();
        readonly Label totalLabel = new Label();
        readonly Label[] unitLabels = { new Label(), new Label(), new Label(), new Label() };
        readonly FlowLayoutPanel rowStart = new FlowLayoutPanel();
        readonly FlowLayoutPanel rowEnd = new FlowLayoutPanel();
        readonly ComboBox cmbMode = new ComboBox();
        readonly ComboBox cmbLang = new ComboBox();
        readonly Label status = new Label();
        readonly Label lblLang = new Label();
        readonly Label lblStart = new Label();
        readonly Label lblEnd = new Label();
        readonly Label lblMode = new Label();
        readonly Label lblOverlap = new Label();
        readonly Label lblOverlapSec = new Label();
        readonly NumericUpDown overlap = new NumericUpDown();
        readonly GroupBox trimBox = new GroupBox();
        readonly Button btnAdd = new Button();
        readonly Button btnRemove = new Button();
        readonly Button btnUp = new Button();
        readonly Button btnDown = new Button();
        readonly Button btnApply = new Button();
        readonly Button btnReset = new Button();
        readonly Button btnSaveOne = new Button();
        readonly Button btnPlay = new Button();
        readonly Button btnPlayAll = new Button();
        readonly Button btnAdv = new Button();
        readonly Button btnMerge = new Button();
        readonly Button btnHelp = new Button();
        readonly Button btnDebt = new Button();
        Form helpForm;
        readonly TextBox helpBox = new TextBox();
        readonly Button helpClose = new Button();
        readonly Button btnSmaller = new Button();
        readonly Button btnLarger = new Button();
        readonly ToolTip tip = new ToolTip();
        readonly FlowLayoutPanel trimFlow = new FlowLayoutPanel();
        readonly FlowLayoutPanel trimButtons = new FlowLayoutPanel();
        readonly Panel bottom = new Panel();
        readonly Panel right = new Panel();
        readonly FlowLayoutPanel listBar = new FlowLayoutPanel();
        Panel helpBar;
        Font baseFont;
        static readonly float[] Levels = { 0.85f, 1f, 1.15f, 1.3f, 1.5f, 1.75f, 2f };
        int level = 1;   // index into Levels (1 = normal)
        int saveCount;   // how many times a merged song has been saved (kept between runs)
        float TextScale { get { return Levels[level]; } }

        static string SettingsFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MidiTrimMerge.lang"); }
        }

        public MainForm()
        {
            // language: saved choice, otherwise follow the Windows display language
            L.Lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant() == "sr" ||
                     CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant() == "hr" ||
                     CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant() == "bs" ? 1 : 0;
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var parts = File.ReadAllText(SettingsFile).Trim().Split(';');
                    L.Lang = parts[0] == "sr" ? 1 : 0;
                    int lv;
                    if (parts.Length > 1 && int.TryParse(parts[1], out lv)) level = Math.Max(0, Math.Min(Levels.Length - 1, lv));
                    int cnt;
                    if (parts.Length > 2 && int.TryParse(parts[2], out cnt) && cnt > 0) saveCount = cnt;
                }
            }
            catch { }

            baseFont = SystemFonts.MessageBoxFont;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            ClientSize = new Size(980, 600);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => AddFiles((string[])e.Data.GetData(DataFormats.FileDrop));

            // ---- file list (left) ----
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.GridLines = true;
            list.Dock = DockStyle.Fill;
            list.Columns.Add("#", 30);
            list.Columns.Add("", 220);
            list.Columns.Add("", 70);
            list.Columns.Add("", 140);
            list.Columns.Add("", 70, HorizontalAlignment.Center);
            list.Columns.Add("", 55, HorizontalAlignment.Center);   // tempo (BPM)
            list.Columns.Add("", 60, HorizontalAlignment.Center);   // key
            list.SelectedIndexChanged += (s, e) => { OnSelect(); UpdatePlayButtons(); SyncAdvanced(); };

            // drag & drop: reorder songs with the mouse (and still accept .mid files dropped from Explorer)
            list.AllowDrop = true;
            list.ItemDrag += (s, e) =>
            {
                int idx = ((ListViewItem)e.Item).Index;
                list.DoDragDrop(new DataObject("MidiClipIndex", idx), DragDropEffects.Move);
            };
            list.DragEnter += (s, e) => ListDragOver(e);
            list.DragOver += (s, e) => ListDragOver(e);
            list.DragLeave += (s, e) => list.InsertionMark.Index = -1;
            list.DragDrop += (s, e) =>
            {
                list.InsertionMark.Index = -1;
                if (e.Data.GetDataPresent("MidiClipIndex"))
                {
                    int from = (int)e.Data.GetData("MidiClipIndex");
                    bool after;
                    int over = DropTarget(e, out after);
                    if (over < 0) return;
                    MoveClip(from, after ? over + 1 : over);
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    AddFiles((string[])e.Data.GetData(DataFormats.FileDrop));
            };

            listBar.Dock = DockStyle.Bottom;
            listBar.AutoSize = true;
            listBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            listBar.Padding = new Padding(0, 6, 0, 4);
            foreach (var b in new[] { btnAdd, btnRemove, btnUp, btnDown, btnUndo, btnRedo }) SetupButton(b);
            btnAdd.Click += (s, e) => AddDialog();
            btnRemove.Click += (s, e) => RemoveSelected();
            btnUp.Click += (s, e) => MoveItem(-1);
            btnDown.Click += (s, e) => MoveItem(1);
            btnUndo.Click += (s, e) => Undo();
            btnRedo.Click += (s, e) => Redo();
            btnUndo.Margin = new Padding(14, 3, 3, 3);   // a little gap separates undo/redo from the list buttons
            listBar.Controls.AddRange(new Control[] { btnAdd, btnRemove, btnUp, btnDown, btnUndo, btnRedo });

            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 5, 0) };
            left.Controls.Add(list);
            left.Controls.Add(listBar);

            // ---- trim panel (right) ----
            trimBox.Dock = DockStyle.Fill;
            foreach (var b in new[] { btnApply, btnReset, btnSaveOne }) SetupButton(b);
            btnApply.Click += (s, e) => ApplyTrim();
            btnReset.Click += (s, e) => ResetTrim();
            btnSaveOne.Click += (s, e) => SaveSelected();
            SetupButton(btnAdv);
            btnAdv.Click += (s, e) => ShowAdvanced();
            BuildPlayerBar();

            info.AutoSize = true;
            lblStart.AutoSize = true;
            lblEnd.AutoSize = true;
            info.Margin = new Padding(3, 3, 3, 12);
            foreach (var n in new[] { startMin, endMin })
            {
                n.Minimum = 0; n.Maximum = 999; n.DecimalPlaces = 0; n.TextAlign = HorizontalAlignment.Right;
            }
            foreach (var n in new[] { startSec, endSec })
            {
                n.Minimum = 0; n.Maximum = 59.99m; n.DecimalPlaces = 2; n.Increment = 1; n.TextAlign = HorizontalAlignment.Right;
            }
            foreach (var u in unitLabels) u.AutoSize = true;
            var rows = new[] { rowStart, rowEnd };
            var boxes = new[] { new Control[] { startMin, unitLabels[0], startSec, unitLabels[1] },
                                new Control[] { endMin, unitLabels[2], endSec, unitLabels[3] } };
            for (int r = 0; r < 2; r++)
            {
                rows[r].AutoSize = true;
                rows[r].AutoSizeMode = AutoSizeMode.GrowAndShrink;
                rows[r].FlowDirection = FlowDirection.LeftToRight;
                rows[r].WrapContents = false;
                rows[r].Margin = new Padding(0, 0, 0, r == 1 ? 12 : 4);
                rows[r].Controls.AddRange(boxes[r]);
            }
            trimButtons.AutoSize = true;
            trimButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            trimButtons.FlowDirection = FlowDirection.LeftToRight;
            trimButtons.WrapContents = false;
            trimButtons.Margin = new Padding(0, 0, 0, 8);
            trimButtons.Controls.AddRange(new Control[] { btnApply, btnReset });

            trimFlow.Dock = DockStyle.Fill;
            trimFlow.FlowDirection = FlowDirection.TopDown;
            trimFlow.WrapContents = false;
            trimFlow.Padding = new Padding(8, 6, 8, 6);
            trimFlow.AutoScroll = true;
            trimFlow.Controls.AddRange(new Control[] { info, lblStart, rowStart, lblEnd, rowEnd, trimButtons, btnSaveOne });
            trimBox.Controls.Add(trimFlow);

            // faint musical note in the background of the trim panel
            trimFlow.BackColor = Color.Transparent;
            foreach (var c in new Control[] { info, lblStart, lblEnd, rowStart, rowEnd, trimButtons }) c.BackColor = Color.Transparent;
            foreach (var u in unitLabels) u.BackColor = Color.Transparent;
            trimBox.Resize += (s, e) => trimBox.Invalidate();
            trimBox.Paint += (s, e) =>
            {
                float size = Math.Min(trimBox.Width * 0.8f, trimBox.Height * 0.4f);
                if (size < 20) return;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using (var f = new Font("Segoe UI Symbol", size, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var br = new SolidBrush(Color.FromArgb(38, 47, 109, 181)))
                {
                    var sz = e.Graphics.MeasureString("♫", f);
                    float noteX = trimBox.Width - sz.Width * 0.9f;
                    e.Graphics.DrawString("♫", f, br, noteX, trimBox.Height - sz.Height * 0.95f);

                    // decorative signature, two rows, to the left of the note
                    string face = ScriptFace();
                    float avail = noteX - 34;
                    if (avail < 40) return;
                    using (var probe = new Font(face, 20, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        float w = e.Graphics.MeasureString("Мирослав", probe).Width;
                        float px = Math.Min(20 * avail / w, size * 0.24f);
                        using (var sf = new Font(face, px, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var tb = new SolidBrush(Color.FromArgb(62, 31, 78, 140)))
                        {
                            float lineH = sf.GetHeight(e.Graphics);
                            float bottom = trimBox.Height - 6;
                            e.Graphics.DrawString("Мирослав", sf, tb, 10, bottom - lineH * 2 + lineH * 0.1f);
                            e.Graphics.DrawString("Берјан", sf, tb, 10 + px * 0.9f, bottom - lineH + lineH * 0.1f);
                        }
                    }
                }
            };

            right.Dock = DockStyle.Right;
            right.Width = 310;
            right.Padding = new Padding(5, 10, 10, 0);
            right.Controls.Add(trimBox);
            totalLabel.Dock = DockStyle.Bottom;
            totalLabel.AutoSize = true;
            totalLabel.Padding = new Padding(4, 10, 4, 4);
            right.Controls.Add(totalLabel);

            // ---- bottom bar ----
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 78;
            cmbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbMode.Items.Add(""); cmbMode.Items.Add("");
            cmbMode.SelectedIndex = 0;
            cmbMode.SelectedIndexChanged += (s, e) =>
            {
                PushUndo("global");
                overlap.Enabled = chkFade.Enabled = cmbMode.SelectedIndex == 0;
                dataVersion++;
                UpdateTotal();
            };
            SetupButton(btnMerge);
            lblOverlap.AutoSize = true;
            lblOverlapSec.AutoSize = true;
            chkFade.AutoSize = true;
            chkFade.Checked = true;
            chkFade.CheckedChanged += (s, e) => { PushUndo("global"); dataVersion++; UpdateTotal(); };
            overlap.Minimum = 0; overlap.Maximum = 60; overlap.DecimalPlaces = 1; overlap.Increment = 0.5m;
            overlap.TextAlign = HorizontalAlignment.Right;
            overlap.ValueChanged += (s, e) => { PushUndo("global"); dataVersion++; UpdateTotal(); };

            lblLang.AutoSize = true;
            lblMode.AutoSize = true;
            cmbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbLang.Items.Add("English"); cmbLang.Items.Add("Srpski");
            cmbLang.Width = 90;
            cmbLang.SelectedIndex = L.Lang;
            cmbLang.SelectedIndexChanged += (s, e) => { L.Lang = cmbLang.SelectedIndex; SaveLang(); ApplyLanguage(); };

            SetupButton(btnHelp);
            btnHelp.Click += (s, e) => ShowHelp();
            SetupButton(btnDebt);
            btnDebt.Click += (s, e) => ShowDebt();
            btnSmaller.Text = "A-"; btnLarger.Text = "A+";
            SetupButton(btnSmaller); SetupButton(btnLarger);
            btnSmaller.MinimumSize = btnLarger.MinimumSize = new Size(10, 10);
            btnLarger.Font = new Font(baseFont, FontStyle.Bold);
            btnSmaller.Click += (s, e) => ChangeLevel(-1);
            btnLarger.Click += (s, e) => ChangeLevel(1);
            status.AutoSize = false;
            status.AutoEllipsis = true;
            bottom.Controls.AddRange(new Control[] { lblMode, cmbMode, lblOverlap, overlap, lblOverlapSec, chkFade, btnAdv, btnMerge, lblLang, cmbLang, btnDebt, btnSmaller, btnLarger, btnHelp, status });
            btnMerge.Click += (s, e) => MergeAll();
            bottom.Resize += (s, e) => LayoutBottom();

            // thin separator lines above the player bar and the bottom bar
            bottom.Paint += (s, e) => e.Graphics.DrawLine(SystemPens.ControlDark, 0, 0, bottom.Width, 0);

            Controls.Add(left);
            Controls.Add(right);
            Controls.Add(playerBar);
            Controls.Add(bottom);

            ApplyScale();
            ApplyLanguage();
            ApplySavedSettings();
            InitSafety();
            RestoreAutosave();
            ClearUndo();
            SetStatus(L.T("statusHelp"));
        }

        // Window showing how much is owed to Milan: 1 KM for every merged song that was saved.
        void ShowDebt()
        {
            using (var dlg = new Form())
            {
                dlg.Text = L.T("debtTitle");
                dlg.Font = Font;
                dlg.Icon = Icon;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.StartPosition = FormStartPosition.CenterParent;

                var title = new Label { Text = L.T("debtLine"), AutoSize = true, Padding = new Padding(24, 20, 24, 0) };
                var amount = new Label
                {
                    Text = saveCount + " KM",
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, Font.Size * 2.6f, FontStyle.Bold),
                    ForeColor = saveCount > 0 ? Color.FromArgb(180, 30, 30) : SystemColors.ControlText,
                    Padding = new Padding(24, 4, 24, 4)
                };
                var note = new Label { Text = L.T("debtNote", saveCount), AutoSize = true, Padding = new Padding(24, 0, 24, 8) };
                var close = new Button { Text = L.T("close"), AutoSize = true, MinimumSize = new Size((int)(90 * TextScale), (int)(28 * TextScale)) };
                close.Click += (s, e) => dlg.Close();

                var flow = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill
                };
                close.Margin = new Padding(24, 4, 24, 16);
                flow.Controls.AddRange(new Control[] { title, amount, note, close });
                dlg.Controls.Add(flow);
                dlg.AutoSize = true;
                dlg.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                dlg.AcceptButton = close;
                dlg.CancelButton = close;
                dlg.ShowDialog(this);
            }
        }

        // Small modal message shown after every merge & save; closes by itself after 2 seconds.
        void ShowSaveNotice()
        {
            using (var dlg = new Form())
            using (var timer = new Timer { Interval = 2000 })
            {
                dlg.Text = Text;
                dlg.Font = Font;
                dlg.Icon = Icon;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.ControlBox = false;
                dlg.MaximizeBox = dlg.MinimizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.StartPosition = FormStartPosition.CenterParent;
                var lbl = new Label
                {
                    Text = "Dodješ zetu Milanu " + saveCount + "KM",
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold),
                    Padding = new Padding(24, 22, 24, 22)
                };
                dlg.Controls.Add(lbl);
                dlg.ClientSize = lbl.PreferredSize;
                timer.Tick += (s, e) => { timer.Stop(); dlg.Close(); };
                dlg.Shown += (s, e) => timer.Start();
                dlg.ShowDialog(this);
            }
        }

        static string scriptFace;
        // a handwriting-style font that exists on both Windows 7 and Windows 11
        static string ScriptFace()
        {
            if (scriptFace != null) return scriptFace;
            var installed = new System.Drawing.Text.InstalledFontCollection().Families.Select(x => x.Name).ToList();
            foreach (var n in new[] { "Segoe Script", "Monotype Corsiva", "Lucida Handwriting", "Brush Script MT", "Georgia" })
                if (installed.Contains(n)) { scriptFace = n; return n; }
            scriptFace = "Times New Roman";
            return scriptFace;
        }

        static Size Pref(Control c) { return c.GetPreferredSize(Size.Empty); }

        void ChangeLevel(int d)
        {
            int n = Math.Max(0, Math.Min(Levels.Length - 1, level + d));
            if (n == level) return;
            level = n;
            SaveLang();
            ApplyScale();
        }

        // Applies the current text size to the whole application (main window and help window).
        void ApplyScale()
        {
            float f = TextScale;
            SuspendLayout();
            Font = new Font(baseFont.FontFamily, baseFont.Size * f, baseFont.Style);
            btnLarger.Font = new Font(Font, FontStyle.Bold);
            totalLabel.Font = new Font(Font.FontFamily, Font.Size * 1.15f, FontStyle.Bold);
            totalLabel.MaximumSize = new Size((int)(300 * f), 0);

            var area = Screen.FromControl(this).WorkingArea;
            int minW = Math.Min((int)(860 * f), area.Width - 20);
            int minH = Math.Min((int)(560 * f), area.Height - 60);
            MinimumSize = new Size(minW, minH);
            ClientSize = new Size(Math.Max(ClientSize.Width, Math.Min((int)(980 * f), area.Width - 40)),
                                  Math.Max(ClientSize.Height, Math.Min((int)(600 * f), area.Height - 100)));

            // keep the window fully on screen after it grew
            var bounds = Bounds;
            Location = new Point(Math.Max(area.Left, Math.Min(bounds.Left, area.Right - bounds.Width)),
                                 Math.Max(area.Top, Math.Min(bounds.Top, area.Bottom - bounds.Height)));

            foreach (var b in new[] { btnAdd, btnRemove, btnUp, btnDown, btnApply, btnReset, btnSaveOne, btnMerge, btnHelp, btnDebt, helpClose, btnPlay, btnPlayAll, btnAdv, btnUndo, btnRedo, btnSetStart, btnSetEnd, btnLive })
                b.MinimumSize = new Size((int)(70 * f), (int)(28 * f));
            btnSmaller.MinimumSize = btnLarger.MinimumSize = new Size((int)(36 * f), (int)(28 * f));
            btnSmaller.Enabled = level > 0;
            btnLarger.Enabled = level < Levels.Length - 1;

            right.Width = (int)(310 * f);
            var wrap = new Size(right.Width - (int)(60 * f), 0);
            info.MaximumSize = lblStart.MaximumSize = lblEnd.MaximumSize = wrap;
            startMin.Width = endMin.Width = (int)(60 * f);
            startSec.Width = endSec.Width = (int)(72 * f);
            foreach (var u in unitLabels) u.Margin = new Padding(2, (int)(5 * f), 8, 0);
            list.Columns[0].Width = (int)(30 * f);
            list.Columns[1].Width = (int)(190 * f);
            list.Columns[2].Width = (int)(65 * f);
            list.Columns[3].Width = (int)(140 * f);
            list.Columns[4].Width = (int)(70 * f);
            list.Columns[5].Width = (int)(55 * f);
            list.Columns[6].Width = (int)(60 * f);

            LayoutPlayerBar();
            ScaleAdvanced();
            if (helpForm != null && !helpForm.IsDisposed)
            {
                helpForm.Font = Font;
                helpClose.MinimumSize = new Size((int)(90 * f), (int)(28 * f));
                LayoutHelpBar();
            }
            ResumeLayout(true);
            LayoutBottom();
        }

        void LayoutBottom()
        {
            float f = TextScale;
            int h = Math.Max(Pref(btnMerge).Height, (int)(28 * f));
            int row1 = 8;
            int W = bottom.ClientSize.Width;

            lblMode.Left = 12;
            lblMode.Top = row1 + (h - Pref(lblMode).Height) / 2;
            cmbMode.Left = lblMode.Left + Pref(lblMode).Width + 8;
            cmbMode.Width = (int)(330 * f);
            cmbMode.Top = row1 + (h - Pref(cmbMode).Height) / 2;
            btnMerge.Left = cmbMode.Right + 12;
            btnMerge.Top = row1;

            cmbLang.Left = W - cmbLang.Width - 12;
            cmbLang.Top = row1 + (h - Pref(cmbLang).Height) / 2;
            lblLang.Left = cmbLang.Left - Pref(lblLang).Width - 6;
            lblLang.Top = row1 + (h - Pref(lblLang).Height) / 2;
            btnDebt.Top = row1;
            btnDebt.Left = lblLang.Left - 14 - Math.Max(Pref(btnDebt).Width, btnDebt.MinimumSize.Width);

            int rowO = row1 + h + 8;
            lblOverlap.Left = 12;
            lblOverlap.Top = rowO + (h - Pref(lblOverlap).Height) / 2;
            overlap.Width = (int)(70 * f);
            overlap.Left = lblOverlap.Left + Pref(lblOverlap).Width + 8;
            overlap.Top = rowO + (h - Pref(overlap).Height) / 2;
            lblOverlapSec.Left = overlap.Right + 4;
            lblOverlapSec.Top = rowO + (h - Pref(lblOverlapSec).Height) / 2;
            chkFade.Left = lblOverlapSec.Right + 18;
            chkFade.Top = rowO + (h - Pref(chkFade).Height) / 2;

            btnAdv.Top = rowO;
            btnAdv.Left = W - Math.Max(Pref(btnAdv).Width, btnAdv.MinimumSize.Width) - 12;

            int row2 = rowO + h + 8;
            int hh = Math.Max(Pref(btnHelp).Height, (int)(28 * f));
            btnHelp.Top = btnSmaller.Top = btnLarger.Top = row2;
            btnHelp.Left = W - Pref(btnHelp).Width - 12;
            btnLarger.Left = btnHelp.Left - 12 - Math.Max(Pref(btnLarger).Width, btnLarger.MinimumSize.Width);
            btnSmaller.Left = btnLarger.Left - 4 - Math.Max(Pref(btnSmaller).Width, btnSmaller.MinimumSize.Width);
            status.Left = 12;
            status.Height = Pref(status).Height > 0 ? Math.Max(Font.Height + 2, 18) : 18;
            status.Top = row2 + (hh - status.Height) / 2;
            status.Width = Math.Max(100, btnSmaller.Left - 20);
            bottom.Height = row2 + hh + 8;
        }

        void LayoutHelpBar()
        {
            if (helpBar == null) return;
            int bh = Math.Max(Pref(helpClose).Height, (int)(28 * TextScale));
            helpBar.Height = bh + 16;
            helpClose.Top = 8;
            helpClose.Left = helpBar.ClientSize.Width - Pref(helpClose).Width - 12;
        }

        void FillHelp()
        {
            helpForm.Text = L.T("helpTitle");
            helpBox.Text = L.T("helpText");
            helpBox.SelectionStart = 0; helpBox.SelectionLength = 0;
            helpClose.Text = L.T("close");
        }

        void ShowHelp()
        {
            if (helpForm != null && !helpForm.IsDisposed)
            {
                if (!helpForm.Visible) helpForm.Show(this);
                helpForm.Activate();
                return;
            }
            helpForm = new Form
            {
                Font = Font,
                ClientSize = new Size(Math.Min((int)(560 * TextScale), Screen.FromControl(this).WorkingArea.Width - 40),
                                      Math.Min((int)(480 * TextScale), Screen.FromControl(this).WorkingArea.Height - 100)),
                MinimumSize = new Size(420, 320),
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                Icon = Icon
            };
            // Closing would dispose helpBox/helpClose (fields reused next time), so only hide the window.
            helpForm.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.None) { e.Cancel = true; helpForm.Hide(); }
            };
            helpBox.Multiline = true;
            helpBox.ReadOnly = true;
            helpBox.ScrollBars = ScrollBars.Vertical;
            helpBox.BackColor = SystemColors.Window;
            helpBox.Dock = DockStyle.Fill;
            helpBox.TabStop = false;
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            helpBar = bar;
            SetupButton(helpClose);
            helpClose.MinimumSize = new Size((int)(90 * TextScale), (int)(28 * TextScale));
            helpClose.Click += (s, e) => helpForm.Close();
            bar.Controls.Add(helpClose);
            bar.Resize += (s, e) => LayoutHelpBar();
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 0) };
            pad.Controls.Add(helpBox);
            helpForm.Controls.Add(pad);
            helpForm.Controls.Add(bar);
            helpForm.AcceptButton = helpClose;
            helpForm.CancelButton = helpClose;
            FillHelp();
            LayoutHelpBar();
            helpForm.Show(this);
        }

        void SaveLang()
        {
            try { File.WriteAllText(SettingsFile, (L.Lang == 1 ? "sr" : "en") + ";" + level + ";" + saveCount); } catch { }
        }

        static void SetupButton(Button b)
        {
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(70, 28);
            b.Padding = new Padding(6, 0, 6, 0);
        }

        void ApplyLanguage()
        {
            Text = L.T("title") + " - Od zeta za Miroslava";
            lblLang.Text = L.T("language");
            list.Columns[1].Text = L.T("colFile");
            list.Columns[2].Text = L.T("colLen");
            list.Columns[3].Text = L.T("colUsed");
            list.Columns[4].Text = L.T("colKar");
            list.Columns[5].Text = L.T("colBpm");
            list.Columns[6].Text = L.T("colKey");
            btnUndo.Text = L.T("undo");
            btnRedo.Text = L.T("redo");
            chkFade.Text = L.T("fade");
            tip.SetToolTip(chkFade, L.T("fadeTip"));
            FillPlayerBar();
            btnAdd.Text = L.T("add");
            btnRemove.Text = L.T("remove");
            btnUp.Text = L.T("up");
            btnDown.Text = L.T("down");
            trimBox.Text = L.T("trimBox");
            lblStart.Text = L.T("start");
            lblEnd.Text = L.T("end");
            unitLabels[0].Text = unitLabels[2].Text = L.T("unitMin");
            unitLabels[1].Text = unitLabels[3].Text = L.T("unitSec");
            btnApply.Text = L.T("apply");
            btnReset.Text = L.T("reset");
            btnSaveOne.Text = L.T("saveOne");
            lblMode.Text = L.T("mode");
            lblOverlap.Text = L.T("overlap");
            lblOverlapSec.Text = L.T("overlapSec");
            tip.SetToolTip(overlap, L.T("overlapTip"));
            tip.SetToolTip(lblOverlap, L.T("overlapTip"));
            btnMerge.Text = L.T("merge");
            btnHelp.Text = L.T("helpBtn");
            btnDebt.Text = L.T("debtBtn");
            btnAdv.Text = L.T("advBtn");
            UpdatePlayButtons();
            FillAdvanced();
            if (helpForm != null && !helpForm.IsDisposed) FillHelp();
            int sel = cmbMode.SelectedIndex;
            undoSuppress++;   // renaming the items must not count as a change
            cmbMode.Items[0] = L.T("modeSeq");
            cmbMode.Items[1] = L.T("modeLayer");
            cmbMode.SelectedIndex = sel < 0 ? 0 : sel;
            undoSuppress--;
            int idx = list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0];
            Refresh(idx);
            SetStatus(L.T("statusHelp"));
            tip.SetToolTip(btnSmaller, L.T("smaller"));
            tip.SetToolTip(btnLarger, L.T("larger"));
            LayoutBottom();
        }

        // ---------- helpers ----------
        void SetStatus(string s) { status.Text = s; }

        void SetTrimEnabled(bool on)
        {
            startMin.Enabled = startSec.Enabled = endMin.Enabled = endSec.Enabled = btnApply.Enabled = btnReset.Enabled = btnSaveOne.Enabled = on;
        }

        static string Fmt(double sec)
        {
            if (sec < 0) sec = 0;
            int m = (int)(sec / 60);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00.00}", m, sec - m * 60);
        }

        static double GetTime(NumericUpDown min, NumericUpDown sec)
        {
            return (double)min.Value * 60 + (double)sec.Value;
        }

        static void SetTime(NumericUpDown min, NumericUpDown sec, double total)
        {
            if (total < 0) total = 0;
            int m = (int)(total / 60);
            double s = Math.Round(total - m * 60, 2);
            if (s >= 60) { m++; s = 0; }
            m = Math.Min(m, (int)min.Maximum);
            min.Value = m;
            sec.Value = (decimal)Math.Min(s, (double)sec.Maximum);
        }

        static bool TryParseTime(string text, out double sec)
        {
            sec = 0;
            text = (text ?? "").Trim().Replace(',', '.');
            if (text.Length == 0) return true;
            double m = 0, s;
            int c = text.IndexOf(':');
            if (c >= 0)
            {
                if (!double.TryParse(text.Substring(0, c), NumberStyles.Float, CultureInfo.InvariantCulture, out m)) return false;
                text = text.Substring(c + 1);
            }
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out s)) return false;
            sec = m * 60 + s;
            return sec >= 0;
        }

        Clip Selected
        {
            get { return list.SelectedIndices.Count == 0 ? null : clips[list.SelectedIndices[0]]; }
        }

        void Refresh(int select = -1)
        {
            dataVersion++;
            RequestAutosave();
            list.BeginUpdate();
            list.Items.Clear();
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                var it = new ListViewItem((i + 1).ToString());
                it.SubItems.Add(c.Name);
                it.SubItems.Add(Fmt(c.Duration));
                it.SubItems.Add(UsedText(c));
                it.SubItems.Add(KarText(c));
                it.SubItems.Add(BpmText(c));
                it.SubItems.Add(KeyText(c));
                list.Items.Add(it);
            }
            list.EndUpdate();
            if (select >= 0 && select < list.Items.Count) list.Items[select].Selected = true;
            OnSelect();
            UpdateTotal();
            UpdatePlayButtons();
            SyncAdvanced();
        }

        // Text of the "Used part" column: the trimmed range plus any per-song settings.
        static string UsedText(Clip c)
        {
            string s = c.IsTrimmed ? Fmt(c.Start) + " - " + Fmt(c.EffectiveEnd) : L.T("whole");
            var extra = new List<string>();
            if (c.Transpose != 0) extra.Add(c.Transpose.ToString("+0;-0", CultureInfo.InvariantCulture));
            if (c.Speed != 100) extra.Add(c.Speed + "%");
            if (c.Volume != 100) extra.Add("vol " + c.Volume + "%");
            if (c.Programs != null && c.Programs.Count > 0) extra.Add("instr.");   // instruments changed
            return extra.Count > 0 ? s + " (" + string.Join(", ", extra) + ")" : s;
        }

        // Karaoke column: ♫ = has karaoke text, ♫* = text was edited, – = none
        static string KarText(Clip c)
        {
            return c.Karaoke ? (c.Edited != null ? "♫*" : "♫") : "–";
        }

        // Overlap (seconds) for every joint between neighbouring songs: a song's own value, or the general one.
        List<double> Overlaps()
        {
            double general = (double)overlap.Value;
            var res = new List<double>();
            for (int i = 0; i + 1 < clips.Count; i++)
                res.Add(clips[i].OverlapAfter >= 0 ? clips[i].OverlapAfter : general);
            return res;
        }

        // The finished song exactly as it would be saved.
        MidiSequence BuildMerged()
        {
            var prepared = clips.Select(Prepare).ToList();
            if (prepared.Count == 1) return prepared[0];
            return cmbMode.SelectedIndex == 0 ? MidiOps.Concatenate(prepared, Overlaps(), chkFade.Checked) : MidiOps.Overlay(prepared);
        }

        // Shows how long the merged song will be, using the current trims and merge mode.
        void UpdateTotal()
        {
            string value;
            try
            {
                if (clips.Count == 0) value = Fmt(0);
                else { GetSeq(2, false); value = Fmt(cacheTotal[2]); }
            }
            catch { value = "-"; }
            totalLabel.Text = L.T("total") + "\n" + value;
        }

        void OnSelect()
        {
            var c = Selected;
            SetTrimEnabled(c != null);
            if (c == null)
            {
                info.Text = clips.Count == 0 ? L.T("hintEmpty") : L.T("hintSelect");
                SetTime(startMin, startSec, 0);
                SetTime(endMin, endSec, 0);
                return;
            }
            int notes = c.Seq.Tracks.Sum(t => t.Count(e => e.IsNoteOn));
            info.Text = c.Name + "\n" + L.T("infoLen") + ": " + Fmt(c.Duration) + "\n" +
                        L.T("infoTracks") + ": " + c.Seq.Tracks.Count + "   " + L.T("infoNotes") + ": " + notes;
            SetTime(startMin, startSec, c.Start);
            SetTime(endMin, endSec, c.EffectiveEnd);   // defaults to the full length
        }

        static string Localize(Exception ex)
        {
            if (ex is NotSupportedException) return L.T("errSmpte");
            if (ex is InvalidDataException) return L.T("errNotMidi");
            return ex.Message;
        }

        // ---------- actions ----------
        void AddDialog()
        {
            using (var d = new OpenFileDialog { Filter = L.T("midiFiles"), Multiselect = true })
            {
                if (lastOpenDir != null && Directory.Exists(lastOpenDir)) d.InitialDirectory = lastOpenDir;
                if (d.ShowDialog(this) == DialogResult.OK) AddFiles(d.FileNames);
            }
        }

        void AddFiles(IEnumerable<string> paths)
        {
            var errors = new List<string>();
            int added = 0;
            PushUndo("add");
            foreach (var p in paths)
            {
                try
                {
                    clips.Add(MakeClip(p));
                    added++;
                    try { lastOpenDir = Path.GetDirectoryName(p); } catch { }
                }
                catch (Exception ex) { errors.Add(Path.GetFileName(p) + ": " + Localize(ex)); }
            }
            Refresh(clips.Count - 1);
            SetStatus(L.T("added", added, clips.Count));
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join("\n", errors), L.T("loadFail"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void RemoveSelected()
        {
            int i = list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0];
            if (i < 0) return;
            PushUndo("remove");
            clips.RemoveAt(i);
            Refresh(Math.Min(i, clips.Count - 1));
        }

        // Which row the mouse is over while dragging, and whether the drop goes after it.
        int DropTarget(DragEventArgs e, out bool after)
        {
            after = false;
            if (list.Items.Count == 0) return -1;
            var pt = list.PointToClient(new Point(e.X, e.Y));
            var item = list.GetItemAt(Math.Min(Math.Max(pt.X, 2), Math.Max(2, list.ClientSize.Width - 2)), pt.Y);
            if (item == null)
            {
                // empty space below the rows: drop at the end (or at the very top if above the first row)
                var first = list.Items[0].Bounds;
                if (pt.Y < first.Top) return 0;
                after = true;
                return list.Items.Count - 1;
            }
            after = pt.Y > item.Bounds.Top + item.Bounds.Height / 2;
            return item.Index;
        }

        void ListDragOver(DragEventArgs e)
        {
            if (e.Data.GetDataPresent("MidiClipIndex"))
            {
                e.Effect = DragDropEffects.Move;
                bool after;
                int over = DropTarget(e, out after);
                if (over >= 0)
                {
                    list.InsertionMark.Index = over;
                    list.InsertionMark.AppearsAfterItem = after;
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            else e.Effect = DragDropEffects.None;
        }

        // Moves clip 'from' so that it ends up before the row currently at 'insertAt' (insertAt == Count means the end).
        int MoveClip(int from, int insertAt)
        {
            if (from < 0 || from >= clips.Count) return from;
            insertAt = Math.Max(0, Math.Min(clips.Count, insertAt));
            if (from < insertAt) insertAt--;
            if (insertAt == from) return from;
            PushUndo("move");
            var c = clips[from];
            clips.RemoveAt(from);
            clips.Insert(insertAt, c);
            Refresh(insertAt);
            return insertAt;
        }

        void MoveItem(int dir)
        {
            int i = list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0];
            int j = i + dir;
            if (i < 0 || j < 0 || j >= clips.Count) return;
            PushUndo("move");
            var t = clips[i]; clips[i] = clips[j]; clips[j] = t;
            Refresh(j);
        }

        bool ApplyTrim()
        {
            var c = Selected;
            if (c == null) return false;
            double s = GetTime(startMin, startSec);
            double e = GetTime(endMin, endSec);
            if (e >= c.Duration - 0.005) e = 0;   // end box at full length = "to the end"
            if (s >= c.Duration) { MessageBox.Show(this, L.T("startBeyond"), L.T("badTimeT")); return false; }
            if (e > 0 && e <= s) { MessageBox.Show(this, L.T("endBefore"), L.T("badTimeT")); return false; }
            if (Math.Abs(c.Start - s) > 0.005 || Math.Abs(c.End - e) > 0.005) PushUndo("trim");
            c.Start = s;
            c.End = e;
            Refresh(list.SelectedIndices[0]);
            SetStatus(L.T("trimSet", c.Name));
            return true;
        }

        void ResetTrim()
        {
            var c = Selected;
            if (c == null) return;
            PushUndo("trim");
            c.Start = 0; c.End = 0;
            Refresh(list.SelectedIndices[0]);
        }

        // Loads a MIDI file and measures what the list shows about it (length, tempo, key, karaoke).
        static Clip MakeClip(string path)
        {
            var seq = MidiIO.Load(path);
            var c = new Clip { Path = path, Seq = seq, Karaoke = seq.HasKaraoke, Duration = MidiOps.DurationSeconds(seq), Bpm = MidiOps.FirstBpm(seq) };
            int tonic; bool minor, est;
            if (MidiOps.GuessKey(seq, out tonic, out minor, out est)) { c.KeyKnown = true; c.KeyPc = tonic; c.KeyMinor = minor; c.KeyEst = est; }
            return c;
        }

        // The song as it will be used: trimmed, new instruments, volume, transposed, speed changed.
        static MidiSequence Prepare(Clip c)
        {
            var s = MidiOps.Trim(c.Source, c.Start, c.End);
            if (c.Programs != null && c.Programs.Count > 0) s = MidiOps.ChangeInstruments(s, c.Programs);
            if (c.Volume != 100) s = MidiOps.ScaleVelocity(s, c.Volume / 100.0);
            if (c.Transpose != 0) s = MidiOps.Transpose(s, c.Transpose);
            if (c.Speed != 100) s = MidiOps.ChangeSpeed(s, c.Speed / 100.0);
            return s;
        }

        static readonly string[] PcNames = { "C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };

        // Tempo column: the tempo the song will really play at (speed setting included).
        static string BpmText(Clip c)
        {
            return Math.Round(c.Bpm * c.Speed / 100.0).ToString(CultureInfo.InvariantCulture);
        }

        // Key column: includes transposing; "?" = guessed from the notes because the file has no key signature.
        static string KeyText(Clip c)
        {
            if (!c.KeyKnown) return "–";
            int pc = (((c.KeyPc + c.Transpose) % 12) + 12) % 12;
            return PcNames[pc] + (c.KeyMinor ? "m" : "") + (c.KeyEst ? "?" : "");
        }

        string AskSavePath(string suggested)
        {
            using (var d = new SaveFileDialog { Filter = L.T("midiSave"), DefaultExt = "mid", FileName = suggested, OverwritePrompt = true })
            {
                if (lastSaveDir != null && Directory.Exists(lastSaveDir)) d.InitialDirectory = lastSaveDir;
                if (d.ShowDialog(this) != DialogResult.OK) return null;
                try { lastSaveDir = Path.GetDirectoryName(d.FileName); } catch { }
                return d.FileName;
            }
        }

        void SaveSelected()
        {
            var c = Selected;
            if (c == null) return;
            if (!ApplyTrim()) return;   // use the values currently typed in the boxes
            string path = AskSavePath(Path.GetFileNameWithoutExtension(c.Path) + "_trimmed.mid");
            if (path == null) return;
            try
            {
                MidiIO.Save(Prepare(c), path);
                SetStatus(L.T("saved", path));
            }
            catch (Exception ex) { MessageBox.Show(this, Localize(ex), L.T("saveFail"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void MergeAll()
        {
            if (clips.Count < 2)
            {
                MessageBox.Show(this, L.T("need2"), L.T("title"));
                return;
            }
            string path = AskSavePath("merged.mid");
            if (path == null) return;
            try
            {
                var merged = BuildMerged();
                MidiIO.Save(merged, path);
                SetStatus(L.T("merged", clips.Count, path, Fmt(MidiOps.DurationSeconds(merged))));
                saveCount++;
                SaveLang();
                ShowSaveNotice();
            }
            catch (Exception ex) { MessageBox.Show(this, Localize(ex), L.T("mergeFail"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
