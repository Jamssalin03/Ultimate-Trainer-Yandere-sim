"""Extrae sólo YandereSimulator_Data/Managed del latest.zip oficial usando peticiones HTTP por rangos,
sin descargar el paquete completo (~3,5 GB). Uso: python extraer_managed.py <url> <destino>"""
import io, os, sys, time, urllib.request, zipfile

class RemoteFile(io.RawIOBase):
    def __init__(self, url):
        self.url, self.pos = url, 0
        self.size = int(urllib.request.urlopen(urllib.request.Request(url, method="HEAD"), timeout=60).headers["Content-Length"])
    def seekable(self): return True
    def readable(self): return True
    def tell(self): return self.pos
    def seek(self, offset, whence=0):
        self.pos = offset if whence == 0 else self.pos + offset if whence == 1 else self.size + offset
        return self.pos
    def read(self, n=-1):
        if n < 0: n = self.size - self.pos
        if n == 0 or self.pos >= self.size: return b""
        end = min(self.pos + n, self.size) - 1
        for intento in range(5):
            try:
                req = urllib.request.Request(self.url, headers={"Range": f"bytes={self.pos}-{end}"})
                data = urllib.request.urlopen(req, timeout=120).read()
                break
            except Exception:
                if intento == 4: raise
                time.sleep(2 ** intento)
        self.pos += len(data)
        return data
    def readinto(self, b):
        d = self.read(len(b)); b[:len(d)] = d; return len(d)

url, destino = sys.argv[1], sys.argv[2]
z = zipfile.ZipFile(io.BufferedReader(RemoteFile(url), buffer_size=1 << 20))
n = 0
for info in z.infolist():
    if info.filename.startswith("YandereSimulator_Data/Managed/") and not info.is_dir():
        ruta = os.path.join(destino, *info.filename.split("/"))
        os.makedirs(os.path.dirname(ruta), exist_ok=True)
        with z.open(info) as src, open(ruta, "wb") as f: f.write(src.read())
        n += 1
print(f"Extraídos {n} archivos en {destino}")
if n == 0: sys.exit(1)
