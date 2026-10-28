# Optimizări Octoglow — 2026-09-17

Modificările urmăresc eliminarea lucrului repetat și a așteptărilor mari din
bucla care animă matricea. Rutele HTTP, câmpurile existente din `/state`, pinii,
fonturile, efectele și ordinea priorităților au fost păstrate.

## Ce s-a schimbat

- **Hardware Monitor:** dimensiunea firmware-ului se calculează o singură dată
  la pornire. În Arduino ESP32 3.3.4, `ESP.getSketchSize()` apelează
  `esp_image_verify()` asupra imaginii din flash; înainte era apelată la fiecare
  `/hwstate`. Spațiul liber afișat este acum diferența față de partiția în care
  rulează firmware-ul, nu dimensiunea integrală a celeilalte partiții OTA.
- **Pagina web:** corpul gzip rămâne în flash și este trimis prin socket cu
  `MSG_DONTWAIT`, maximum 1460 octeți per client și apel. Sunt permise două
  transferuri simultane; surplusul primește 503 cu `Retry-After: 1`. Un client
  fără progres timp de cinci secunde este închis. ETag și autentificarea rămân.
- **Matrice:** SPI hardware pe CLK=3, DATA=1, CS=2, cu aceeași bibliotecă și
  aceleași cadre. Pentru comparație sau module/cablaje sensibile la frecvența
  SPI, `OCTOGLOW_SOFTWARE_SPI=1` reactivează driverul anterior.
- **Ritm afișaj / HTTP:** animația rulează într-un task FreeRTOS cu prioritate
  mai mare, fixat pe același nucleu cu serverul. Cererile HTTP, transferul gzip,
  fluxul UDP al ecranului și salvarea NVS sunt preemptibile și nu mai fac parte
  din pasul care avansează scroll-ul. Handlerele trimit comenzile vizuale printr-o
  coadă de biți, iar toate scrierile MD_MAX72XX rămân serializate într-un singur
  task. Dacă alocarea taskului eșuează, firmware-ul revine automat la bucla
  cooperativă anterioară.
- **JSON:** `/state` folosește un buffer fix de 1024 octeți pe stivă, în locul
  rezervării de 4224 octeți pe heap, plus creșteri. Numerele întregi și textele
  escapate sunt scrise fără șiruri temporare alocate dinamic.
- **Setări:** se compară cu valorile din NVS și se scriu numai diferențele,
  urmate de un singur `nvs_commit`. Cheile și tipurile sunt compatibile cu
  setările existente. Sunetele Events, Canvas și culorile folosesc aceeași
  amânare de 800 ms ca celelalte setări; modificările vizuale/sonore se aplică
  imediat. O scriere eșuată rămâne programată pentru reîncercare. Repornirea
  controlată păstrează mecanismul de golire a salvărilor în așteptare.
- **RAM temporar:** Weather, Currency și YouTube rulează câte un fetch în loc
  să suprapună cele trei stive de 8 KB și conexiunile lor. Eșecul de creare a
  unui task nu mai lasă funcția blocată permanent în starea „busy”. Publicarea
  rezultatelor folosește flaguri atomice.
- **Portal:** maximum o cerere în curs per endpoint de monitorizare, inclusiv
  citirea corpului JSON, cu timeout de patru secunde. Următoarea verificare se
  programează după terminarea celei precedente. Timer nu mai pornește încă o
  buclă periodică la fiecare apăsare. Polling-ul vizual este suspendat când fila
  este ascunsă. Modemul Wi-Fi rămâne treaz temporar în timpul traficului API;
  aceasta favorizează latența, cu un consum mai mare cât timp interfața este activă.
- **Diagnostic:** `httpMaxUri` este capturat înainte ca WebServer să îl șteargă.
  `pageUs` măsoară cea mai lungă porțiune de lucru la transfer; noul
  `pageTransferUs` măsoară întregul transfer, inclusiv așteptările rețelei.

## Verificări locale

Compilare reușită pe configurația salvată în proiect:
`esp32:esp32:waveshare_esp32_s3_zero:PartitionScheme=min_spiffs,PSRAM=disabled`,
Arduino ESP32 3.3.4, MD_MAX72XX 3.5.1, ArduinoJson 7.4.3.

| Rezultat compilator ESP32-S3 | Original | Optimizat |
|---|---:|---:|
| Spațiu program | 1.491.095 B | 1.484.507 B |
| Variabile globale | 57.976 B | 58.192 B |

Firmware-ul ocupă cu 6.588 B mai puțin în flash. Variabilele globale cresc cu
216 B pentru evidența transferurilor, diagnostic și coordonarea taskurilor.
Taskul de afișare rezervă dinamic o stivă de 6 KB la pornire. Reducerea urmărită pentru
RAM este în alocările temporare și în evitarea fetch-urilor simultane; aceasta
nu trebuie confundată cu valoarea variabilelor globale raportată de compilator.

ESP32-C3: atât originalul, cât și varianta optimizată eșuează la compilare în
aceleași zone preexistente: acces direct la registrele GPIO/RMT ale S3 și
`esp_sleep_enable_ext0_wakeup`. Nu este livrat un binar C3 și nu este declarată
compatibilitatea acestei versiuni cu C3. Adaptarea buzzerului și a sursei de
wake-up la acea placă necesită o modificare separată, inclusiv verificarea pinilor.

Testele C++ rulează codul real din `runtime_support.h` cu adaptoare de test:
scrieri socket parțiale, backpressure, întreruperi, deconectare, expirare inclusiv
la overflow de `millis()`, păstrarea clientului, date binare fără pierderi,
scrieri NVS numai pentru diferențe, erori/reîncercări, JSON lung și escapare.

Testele JavaScript verifică sintaxa tuturor paginilor, deduplicarea cererilor,
timeout-urile, erorile HTTP, fila ascunsă și lipsa buclelor suplimentare Timer.
Pagina demo a fost verificată și în Edge headless: încărcarea setărilor,
Hardware Monitor, navigare și resetare, fără erori JavaScript. Datele din demo
sunt simulate și nu reprezintă măsurători de performanță ale ceasului.

Verificările de compatibilitate compară cele 74 de rute, toate cele 154 de
expresii de persistare anterioare, câmpurile `/state` și decomprimarea exactă a
ambelor resurse gzip. Fișierele generate sunt la zi.

## Validare pe ceas

Compilarea și simulările nu pot măsura FPS-ul, latența Wi-Fi ori heap-ul real.
Nu a fost încărcat firmware pe dispozitiv. După încărcarea variantei pentru
placa potrivită, lasă pornirea să se termine, apoi resetează statisticile și
compară aceleași scenarii cu aceeași animație și rețea:

1. Hardware Monitor deschis 2–3 minute: `loopMaxUs`, `httpMaxUs`, `heapMinFree`.
2. Prima deschidere a paginii, reîncărcare cu cache și deschidere din două file.
3. Comenzi Now Playing/Notifications/ETS2 din Connect concomitent cu portalul.
4. Mai multe schimbări Events, luminozitate și Canvas; așteaptă peste o secundă,
   repornește și verifică păstrarea setărilor.
5. Toate tranzițiile, touch, timer și alarme, inclusiv revenirea din prioritate.

Unele operații rămân deliberat sincrone în taskul care le execută: tranzițiile
existente, pornirea și schimbarea modului Wi-Fi, căutarea unui oraș,
OTA/restaurare și operațiile flash care chiar modifică date. Serverul WebServer
poate aștepta un client lent fără să mai țină pasul de scroll. Operațiile flash
pot opri temporar cache-ul intern al cipului și trebuie măsurate pe hardware.

Implementările bibliotecilor au fost verificate local în versiunile instalate.
Surse upstream: [Esp.cpp](https://github.com/espressif/arduino-esp32/blob/3.3.4/cores/esp32/Esp.cpp),
[MD_MAX72XX](https://majicdesigns.github.io/MD_MAX72XX/class_m_d___m_a_x72_x_x.html).

## Backup și fișiere de verificare

Originalele exacte sunt păstrate în
`C:\Users\Adi\Documents\New project\octoglow-optimization\original`.
Testele și logurile de compilare sunt în directorul părinte `octoglow-optimization`.
`Octoglow-optimized.zip` conține toate sursele necesare. Firmware-ul compilat
pentru configurația S3 de mai sus este `build-s3\Octoglow.ino.bin`.

# Spațiu în flash — 2026-10-01

Firmware-ul a scăzut de la **1.794.447 la 1.584.623 octeți** (de la 91% la 80% din
partiția de aplicație de 1.966.080), adică **209.824 octeți** câștigați. Nicio
funcție, rută, setare sau pagină nu s-a schimbat; `Octoglow.ino`, `portal.html`,
`auth.html` și `onboarding.html` au rămas cum erau. RAM-ul e neschimbat
(66.080 octeți de variabile globale).

| Pas | Mărime | Câștig |
|---|---|---|
| Înainte | 1.794.447 (91%) | |
| Argumente și rute date ca text simplu (`runtime_support.h`) | 1.739.719 (88%) | 54.728 |
| Fără excepții C++ (`build_opt.h`) | 1.638.399 (83%) | 101.320 |
| Pagini minificate și comprimate mai bine (`webmin.py`) | 1.584.623 (80%) | 53.776 |

## Ce s-a făcut

- **`runtime_support.h` — `MeasuredWebServer`.** Sketch-ul numește un argument,
  un antet sau o rută printr-un text literal de vreo 750 de ori:
  `server.arg("x")`, `server.hasArg("x")`, `server.on("/x", HTTP_GET, handleX)`.
  `WebServer` cere acolo un `String` (sau un `Uri` și un `std::function`), așa că
  fiecare rând construia, folosea și distrugea obiectul temporar pe loc.
  Supraîncărcările noi primesc textul ca `const char*` și construiesc
  temporarul o singură dată, în afara rândului. Apelurile din sketch nu s-au
  schimbat și fac același lucru. `handleSettings()` a scăzut de la 38,8 KB la
  9,5 KB, iar `registerRoutes()` de la 14,7 KB la 1,8 KB.
- **`build_opt.h` — `-fno-exceptions`.** Nucleul Arduino ESP32 compilează cu
  excepții C++, deși nici sketch-ul, nici nucleul, nici bibliotecile folosite nu
  au vreun `try`/`catch`/`throw`. Tabelele de derulare (`.eh_frame`,
  `.gcc_except_table`) și codul de curățare ocupau peste 100 KB. O excepție
  aruncată din libstdc++ (memorie epuizată) oprește ceasul și îl repornește, la
  fel ca înainte, pentru că nu o prindea nimeni. Fișierul conține doar opțiuni
  de compilator, fără comentarii (așa îl citește GCC).
- **`builders/webmin.py`, chemat de `builders/build_web.py`.** Înainte de gzip, paginile pierd
  comentariile, indentarea și spațiile fără rol; șirurile, textele și
  atributele rămân octet cu octet. Comprimarea se face cu 7-Zip (sau zopfli)
  când există, altfel cu zlib. Paginile din flash: 222,7 KB → 169,9 KB.

## Cum s-a verificat că nimic nu s-a schimbat

- **Scripturile:** fiecare `<script>` minificat a fost parsat cu esprima (un
  parser JavaScript separat) alături de original: 100.501 tokeni identici, iar
  rândurile noi lipsesc doar unde nu pot însemna nimic. `builders/build_web.py` repetă
  verificarea la fiecare rulare dacă `esprima` e instalat (`pip install esprima`).
- **Documentul și stilurile:** în browser, DOM-ul (7.515 noduri în portal) și
  toate regulile CSS (646) ale paginii minificate sunt identice cu ale sursei,
  pentru portal, login și configurarea inițială.
- **În rulare:** demo-ul minificat și cel original dau, pe toate cele 27 de
  ecrane, același număr de elemente și același text, fără erori.
- **Ce ajunge în `web_assets.h`:** decomprimat, e exact rezultatul verificat.
- **Pe ceas nu s-a testat:** firmware-ul compilează, dar nu a fost încărcat.

## De ținut minte

- **Primul build din Arduino IDE trebuie să fie curat**, ca bibliotecile să fie
  recompilate fără excepții. Dacă IDE-ul arată în jur de 1,62 MB în loc de
  1,59 MB, bibliotecile au venit din cache: închide IDE-ul și șterge
  `%LOCALAPPDATA%\arduino\sketches`, sau schimbă o opțiune din Tools și pune-o
  înapoi. Nucleul Arduino din cache-ul IDE-ului poate adăuga încă ~5 KB.
- `python builders/!buildall.py --no-minify` pune paginile în flash exact cum sunt
  scrise (pentru depanare în browser; ocupă cu ~50 KB mai mult).
- Dacă `builders/webmin.py` nu poate citi un script cu certitudine, pagina intră
  neminificată și scriptul de build spune asta; firmware-ul rămâne corect.
- Fără 7-Zip pe calculator, `builders/build_web.py` folosește zlib și paginile ies cu
  ~7 KB mai mari.
- Ce nu s-a atins, deși ar mai aduce puțin: iconițele SVG repetate (cel mult
  6 KB, dar ar schimba felul în care sunt desenate) și `-Oz` (ar putea încetini
  animația).
