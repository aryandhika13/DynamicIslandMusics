# Dynamic Island Desktop (.NET WPF)

Proyek ini adalah fondasi awal (*boilerplate*) untuk membuat aplikasi desktop bergaya **Dynamic Island** di Windows menggunakan **.NET 8 WPF**.

## 🛠️ Fitur Utama Fondasi
- **Frameless & Transparent Window:** Menghilangkan border bawaan Windows dan memberikan background transparan.
- **Topmost Layer:** Menjaga tampilan selalu berada di atas jendela aplikasi lain.
- **Centering System:** Otomatis memposisikan *island* di tengah-atas layar.
- **Smooth Expansion Animation:** Animasi perubahan ukuran (kecil/besar) menggunakan `Storyboard` WPF.
- **Draggable Feature:** Opsi untuk menggeser jendela jika diperlukan.

---

## 🚀 Petunjuk Setup & Persyaratan

### 1. Prasyarat Software
Sebelum memulai, pastikan Anda telah menginstal:
* **Visual Studio 2022** (versi 17.8 atau lebih baru)
  * Pastikan Workload **".NET desktop development"** di centang saat instalasi Visual Studio.
* **.NET 8.0 SDK** (biasanya terinstal otomatis bersama Visual Studio 2022).

---

### 2. Langkah-Langkah Membuat Proyek di Visual Studio

1. Buka **Visual Studio 2022**.
2. Pilih **Create a new project**.
3. Cari **WPF Application** (pilih yang menggunakan **C#**, bukan `.NET Framework` lama).
4. Beri nama proyek `DynamicIslandDesktop` dan tentukan lokasi penyimpanan.
5. Pada pilihan **Framework**, pilih **.NET 8.0 (Long-term support)**, lalu klik **Create**.

---

### 3. Implementasi Kode

Ganti kode bawaan proyek Anda dengan file yang disediakan:

1. **`MainWindow.xaml`**:
   * Salin kode dari file `MainWindow.xaml` ke dalam proyek Anda untuk mengatur layout visual, transparansi, serta sudut membulat (*rounded corners*).
2. **`MainWindow.xaml.cs`**:
   * Salin kode logic dari file `MainWindow.xaml.cs` untuk menangani peristiwa klik, animasi expand/collapse, serta penempatan jendela di bagian atas layar.

---

### 4. Menjalankan Aplikasi
* Tekan tombol **F5** atau klik ikon **Start/Play** di Visual Studio.
* Pil hitam (*Dynamic Island*) akan muncul di bagian tengah atas layar Anda.
* Klik pada pil tersebut untuk melihat efek **Expand** dan **Collapse** secara halus.

---

## 🔮 Rencana Pengkustoman Selanjutnya
Setelah fondasi ini berjalan, Anda dapat menambahkan modul UI seperti:
- Visualizer musik / pengontrol lagu
- Notifikasi sistem
- Jam & cuaca