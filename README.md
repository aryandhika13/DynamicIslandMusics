# Dynamic Island Desktop

Aplikasi WPF kecil yang menampilkan "Dynamic Island" ala iPhone di tengah atas layar Windows. Island menampilkan lagu yang sedang diputar dari aplikasi apa pun (Spotify, YouTube di browser, dan sebagainya), lengkap dengan kontrol media dan lirik yang berjalan sesuai waktu lagu.

## Fitur

- **Info media otomatis.** Judul, artis, dan album art diambil dari Windows Media Transport Controls, jadi bekerja dengan pemutar media apa pun yang terdaftar di Windows.
- **Compact dan expanded.** Island kecil menampilkan album art (berputar saat musik diputar) dan judul. Arahkan mouse ke bagian tengah island untuk membukanya menjadi tampilan besar dengan tombol Previous, Play/Pause, Next, dan Lirik.
- **Lirik tersinkron.** Diambil dari [LRCLIB](https://lrclib.net) dan ditampilkan baris demi baris mengikuti posisi lagu. Di mode compact, lirik panjang turun ke baris berikutnya (maksimal 3 baris), dan font dikecilkan otomatis kalau masih terlalu panjang.
- **Menu.** Klik ikon di kanan island untuk memunculkan pill **Setting** dan **Keluar**.
- **Pengaturan.** Pill Setting membuka panel di bawah island berisi slider ukuran font lirik. Saat mode lirik aktif, island ikut membesar atau mengecil mengikuti ukuran font yang dipilih.
- **Animasi.** Animasi pembuka dan penutup, expand/collapse, serta rotasi album art.

## Kebutuhan

- Windows 10 (versi 1809 / build 17763) atau lebih baru. API `Windows.Media.Control` hanya ada di versi ini ke atas.
- .NET 6 atau lebih baru dengan target framework Windows, misalnya `net8.0-windows10.0.19041.0`. Sesuaikan dengan proyekmu.
- Paket NuGet [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm):

  ```
  dotnet add package CommunityToolkit.Mvvm
  ```

- Koneksi internet, hanya untuk mengambil lirik.

## Menjalankan

```
dotnet build
dotnet run
```

Atau buka proyek di Visual Studio dan tekan **F5**. Kalau muncul error yang aneh setelah mengganti nama elemen atau file, hapus folder `bin` dan `obj`, lalu *Rebuild Solution*.

## Struktur proyek

```
DynamicIslandDesktop/
├── Models/
│   ├── LyricLine.cs             Satu baris lirik (waktu + teks)
│   ├── MediaInfo.cs             Judul, artis, dan thumbnail lagu
│   └── AppSettings.cs           Pengaturan aplikasi (ukuran font lirik, dll.)
├── Services/
│   ├── MediaSessionService.cs   Pembungkus Windows Media Transport Controls
│   └── LyricsService.cs         Ambil dan parsing lirik dari LRCLIB
├── ViewModels/
│   └── MainViewModel.cs         State yang ditampilkan + command
├── Helpers/
│   └── TextFit.cs               Mengecilkan font agar teks muat
├── MainWindow.xaml              Tampilan island (binding ke ViewModel)
└── MainWindow.xaml.cs           Animasi, posisi window, event UI
```

## Arsitektur: pola MVVM berlapis

Kode dipisah menjadi beberapa layer supaya `MainWindow.xaml.cs` tidak menumpuk. Aturan utamanya, ketergantungan hanya boleh mengarah ke bawah:

```
   View  ──►  ViewModel  ──►  Service  ──►  Model
 (XAML +     (state +        (logika non-UI:  (data
 code-behind) command)        media, lirik)    sederhana)
```

Layer bawah tidak boleh tahu layer di atasnya. Misalnya `LyricsService` tidak tahu ada `MainViewModel`, dan `MainViewModel` tidak tahu ada `TextBlock`. Dengan begitu logika bisa diubah atau dites tanpa menyentuh tampilan.

### 1. Model

**Fungsi:** menyimpan data. Tidak berisi logika berat dan tidak bergantung pada layer lain.

| File | Isi |
|---|---|
| `LyricLine.cs` | `record` berisi waktu dan teks satu baris lirik. |
| `MediaInfo.cs` | `record` berisi judul, artis, dan thumbnail (sudah di-`Freeze`, aman lintas thread). |
| `AppSettings.cs` | Pengaturan yang bisa berubah saat aplikasi berjalan: `LyricFontSize`, `RotateAlbumArt`, `AlwaysOnTop`. Memakai `ObservableObject` supaya perubahan bisa diamati. |

### 2. Service

**Fungsi:** menjalankan logika non-UI, seperti bicara dengan API Windows dan jaringan. Service tidak tahu apa pun tentang View maupun ViewModel; ia hanya menerima parameter dan mengeluarkan hasil atau event.

| File | Tanggung jawab |
|---|---|
| `MediaSessionService.cs` | Membungkus `GlobalSystemMediaTransportControlsSessionManager`. Mendeteksi pergantian pemutar media, membaca info lagu dan thumbnail, mengontrol Play/Pause/Next/Previous, dan menghitung posisi lagu saat ini. Event dari WinRT datang dari thread lain, jadi service mengembalikannya ke thread UI sebelum meneruskannya lewat `MediaChanged` dan `PlaybackChanged`. |
| `LyricsService.cs` | Mengambil lirik tersinkron (format LRC) dari LRCLIB lewat `/api/get`, dengan `/api/search` sebagai cadangan. Mem-parsing LRC menjadi daftar `LyricLine` dan menyediakan `FindIndex` untuk mencari baris yang aktif pada posisi tertentu. |

### 3. ViewModel

**Fungsi:** menjadi jembatan antara Service dan View. Menyimpan state yang ditampilkan dan menyediakan command untuk aksi pengguna. Tidak boleh mereferensikan kontrol UI (`TextBlock`, `Button`, dan sebagainya).

`MainViewModel.cs` berisi:

- **State yang di-*bind* ke XAML:** `Title`, `Artist`, `AlbumArt`, `IsPlaying`, `ShowLyrics`, `CurrentLyric`, `NextLyric`.
- **Properti turunan:** `CompactText` (judul atau lirik, tergantung mode), `PlayPauseGlyph`, serta ukuran font lirik yang berskala dari pengaturan (`CompactFontSize`, `LyricCurrentFontSize`, `LyricNextFontSize`, `LyricMaxHeight`, `LyricScale`).
- **Command:** `PlayPauseCommand`, `NextCommand`, `PrevCommand`, `ToggleLyricsCommand`.
- **Logika lirik:** memuat lirik saat lagu berganti, lalu timer 300 ms memilih baris yang sedang aktif dari posisi lagu.

Boilerplate `INotifyPropertyChanged` dan `ICommand` dihasilkan otomatis oleh CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).

### 4. View

**Fungsi:** semua yang berhubungan dengan tampilan. Tidak memanggil API atau jaringan dan tidak memuat logika bisnis.

| File | Tanggung jawab |
|---|---|
| `MainWindow.xaml` | Susunan visual island, pill menu, dan panel pengaturan. Teks, gambar, dan tombol di-*bind* ke ViewModel. Pergantian tampilan lirik/judul diatur lewat `DataTrigger`. |
| `MainWindow.xaml.cs` | Hal-hal yang tidak cocok untuk binding: animasi pembuka dan penutup, animasi expand/collapse, rotasi album art, posisi window, menu (pill Setting dan Keluar), panel pengaturan, serta perhitungan ukuran island. Bereaksi terhadap perubahan ViewModel dan `AppSettings` lewat `PropertyChanged`. |

MVVM tidak melarang code-behind. Yang dilarang hanya logika bisnis di sana, sehingga animasi tetap pada tempatnya di View.

### 5. Helpers

**Fungsi:** utilitas UI yang bisa dipakai ulang dan tidak termasuk layer mana pun di atas.

`TextFit.cs` adalah *attached property* (`TextFit.MaxFontSize`) yang mengecilkan font `TextBlock` sampai teksnya muat. Untuk teks satu baris, ukurannya disesuaikan dengan lebar. Untuk teks yang membungkus, ukurannya disesuaikan dengan `MaxHeight`. Method `Compute` juga dipakai `MainWindow.xaml.cs` untuk menghitung tinggi island saat lirik membungkus ke bawah.

## Alur data

**Lagu berganti:**

```
Pemutar media ─► MediaSessionService ─► event MediaChanged
                                              │
                                              ▼
                                        MainViewModel   (isi Title, Artist, AlbumArt)
                                              │ binding
                                              ▼
                                         MainWindow.xaml
```

**Lirik:**

```
ToggleLyricsCommand ─► MainViewModel ─► LyricsService ─► LRCLIB
                           │
              timer 300 ms │ posisi lagu dari MediaSessionService
                           ▼
              CurrentLyric / NextLyric ─► binding ─► tampilan
```

**Tombol kontrol:** tombol di XAML memanggil Command di ViewModel, yang meneruskannya ke `MediaSessionService` (Play, Pause, Next, atau Previous).

**Pengaturan:**

```
Slider (XAML) ─► AppSettings.LyricFontSize ─► MainViewModel (hitung ukuran font)
                          │
                          └─► MainWindow.xaml.cs (animasikan ukuran island)
```

## Menambah fitur baru

- **Pengaturan baru:** tambahkan properti di `AppSettings`, buat kontrolnya di panel pengaturan (`MainWindow.xaml`), lalu tangani efek visualnya di `Settings_PropertyChanged` (`MainWindow.xaml.cs`).
- **Sumber lirik lain:** buat service baru atau ubah `LyricsService`. ViewModel dan View tidak perlu tahu dari mana lirik berasal.
- **Data atau state baru di tampilan:** tambahkan properti di `MainViewModel`, lalu bind di XAML.
- **Animasi atau perilaku visual baru:** tulis di `MainWindow.xaml.cs`.

## Catatan

- Pengaturan belum disimpan, jadi kembali ke nilai awal setiap aplikasi dibuka. Penyimpanan ke file JSON bisa ditambahkan di layer Service (misalnya `SettingsService`).
- `MainViewModel` masih memakai `DispatcherTimer` dan `ImageSource` dari WPF demi kesederhanaan. Kalau ingin ViewModel bebas dari WPF sepenuhnya, timer bisa dipindah ke View atau dibungkus interface.
- Lirik bergantung pada ketersediaan data di LRCLIB. Lagu yang tidak ditemukan menampilkan "Lirik tidak ditemukan".
