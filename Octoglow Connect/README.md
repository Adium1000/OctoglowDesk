# Octoglow Connect 2.0

Octoglow Connect este acum hub-ul comun pentru **Octoglow Clock** și
**Octoglow Macro**.

## Ce este implementat

- listă dinamică de dispozitive în meniul din stânga;
- detectare automată Octoglow Clock în rețeaua locală prin endpoint-ul firmware
  `/authstate`, plus adresa salvată și adresa AP `192.168.4.1`;
- detectare hot-plug pentru Octoglow Macro prin USB HID;
- o pagină Octoglow Clock cu taburi separate pentru overview, integrări și activity
  log; setările Video/Music Now Playing și Euro Truck Simulator 2 sunt legate de
  acest dispozitiv;
- setările globale controlează doar aplicația: limba, tema, backdrop-ul, pornirea
  cu Windows și comportamentul din system tray;
- o pagină Macro cu VID/PID, revizie USB, layout fizic și highlight live pentru
  cele 7 taste și encoder;
- sender-ul Octoglow Clock rulează la nivel de aplicație și nu mai depinde de pagina
  deschisă;
- un singur router Win32 pentru tray și Raw Input, pentru a evita conflictele
  dintre hook-urile ferestrei.

## Octoglow Macro

Firmware-ul V4 permite configurarea tastelor, encoderului, LED-ului și a până la
cinci taburi. Acțiunile HID funcționează independent de Connect. Pentru lansarea
unei aplicații, firmware-ul păstrează doar ID-ul, iar calea rămâne pe calculator.
Connect găsește automat canalul USB de configurare al aceluiași dispozitiv și
verifică versiunea firmware-ului înainte de a trimite setări. Dacă pe placă este
încă V3, trebuie instalat `../Octoglow Macro/Firmware/Firmware.ino`.

## Build

```powershell
dotnet build .\OctoglowSender.csproj -c Release
```

Executabilul rezultat se numește `OctoglowConnect.exe`.
