# Dynamic Island Desktop

Dynamic Island ala iPhone untuk desktop Windows. Aplikasi WPF ringan ini menampilkan lagu yang sedang kamu dengarkan di bagian tengah atas layar, lengkap dengan kontrol pemutar dan lirik yang berjalan sesuai lagu.

<!-- Tambahkan screenshot atau GIF demo di sini, misalnya: ![Demo](docs/demo.gif) -->

## Fitur

- **Mengikuti musik apa pun.** Menampilkan judul, artis, dan sampul album dari pemutar media yang sedang aktif, seperti Spotify atau YouTube di browser.
- **Kontrol pemutar.** Putar, jeda, lanjut ke lagu berikutnya, atau kembali ke lagu sebelumnya langsung dari island.
- **Dua tampilan.** Tampilan ringkas yang tidak mengganggu, dan tampilan lengkap yang terbuka saat kamu mengarahkan mouse ke island.
- **Lirik waktu nyata.** Lirik tampil baris demi baris, mengikuti posisi lagu.
- **Ukuran font dapat diatur.** Sesuaikan ukuran lirik lewat menu Setting, dan island ikut menyesuaikan ukurannya.
- **Animasi halus.** Transisi yang mulus saat island muncul, berubah ukuran, dan menutup.
- **Selalu terlihat.** Island tetap berada di atas jendela lain, tanpa menghalangi pekerjaanmu.

## Persyaratan

- Windows 10 versi 1809 (build 17763) atau lebih baru
- [.NET SDK](https://dotnet.microsoft.com/download) 6 atau lebih baru
- Koneksi internet (hanya untuk mengambil lirik)

## Memulai

```
git clone <url-repositori>
cd DynamicIslandDesktop
dotnet run
```

Kamu juga bisa membuka proyek di Visual Studio, lalu menjalankannya dengan **F5**.

## Cara pakai

1. Putar musik di aplikasi apa pun yang mendukung kontrol media Windows. Judul dan sampul album langsung tampil di island.
2. Arahkan mouse ke bagian tengah island untuk membuka tampilan lengkap, lalu gunakan tombol kontrol yang tersedia.
3. Klik tombol lirik untuk menampilkan lirik, dan klik lagi untuk kembali ke judul lagu.
4. Klik ikon di sisi kanan island untuk membuka menu:
   - **Setting** untuk mengatur ukuran font lirik.
   - **Keluar** untuk menutup aplikasi.

## Arsitektur

Proyek ini memakai pola **MVVM**. Kodenya dibagi menjadi beberapa layer, dan ketergantungan hanya mengarah ke bawah:

```
View  ──►  ViewModel  ──►  Service  ──►  Model
```

| Layer | Fungsi |
|---|---|
| **View** (`MainWindow`) | Tampilan, animasi, dan interaksi pengguna. Tidak berisi logika bisnis. |
| **ViewModel** | Menyimpan state yang ditampilkan (judul, lirik, status putar) dan menyediakan command untuk aksi pengguna. |
| **Service** | Logika di luar UI: membaca sesi media Windows dan mengambil lirik dari internet. |
| **Model** | Data sederhana, seperti baris lirik, info lagu, dan pengaturan. |
| **Helpers** | Utilitas khusus WPF, seperti penyesuaian ukuran teks dan konversi gambar. |

Dengan pemisahan ini, logika bisa diubah atau diuji tanpa menyentuh tampilan. Model, Service, dan ViewModel tidak bergantung pada tipe WPF.

```
DynamicIslandDesktop/
├── Models/          Data: LyricLine, MediaInfo, AppSettings
├── Services/        MediaSessionService, LyricsService, ITicker
├── ViewModels/      MainViewModel
├── Helpers/         TextFit, DispatcherTicker, BytesToImageConverter
├── MainWindow.xaml      Tampilan island
└── MainWindow.xaml.cs   Animasi dan event UI
```

## Teknologi

- [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/) dan .NET
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) untuk MVVM
- Windows Media Transport Controls (`Windows.Media.Control`) untuk membaca dan mengontrol media
- [LRCLIB](https://lrclib.net) untuk lirik tersinkron

## Keterbatasan

- Hanya berjalan di Windows 10 dan 11.
- Lirik bergantung pada ketersediaan data di LRCLIB, jadi tidak semua lagu memilikinya.
- Pengaturan belum tersimpan dan kembali ke nilai awal setiap aplikasi dibuka.

## Ucapan terima kasih

Lirik disediakan oleh [LRCLIB](https://lrclib.net).