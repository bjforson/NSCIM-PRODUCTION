import psycopg2, os, struct

pw = os.environ.get('NICKSCAN_DB_PASSWORD', '')
conn = psycopg2.connect(host='localhost', dbname='nickscan_production', user='postgres', password=pw)
cur = conn.cursor()

cur.execute("""
    SELECT a.scanimage, a.imagedisplayname
    FROM crossrecordscans crs
    JOIN asescans a ON a.id = crs.scannerrecordid
    WHERE a.scanimage IS NOT NULL
    LIMIT 3
""")

for row in cur.fetchall():
    data = bytes(row[0])
    name = row[1]
    magic = data[:16].hex()
    size = len(data)

    # Detect format from magic bytes
    fmt = "unknown"
    if data[:2] == b'\xff\xd8':
        fmt = "JPEG"
    elif data[:8] == b'\x89PNG\r\n\x1a\n':
        fmt = "PNG"
    elif data[:4] in (b'II\x2a\x00', b'MM\x00\x2a'):
        fmt = "TIFF"
    elif data[:2] == b'BM':
        fmt = "BMP"
    elif data[:4] == b'RIFF':
        fmt = "RIFF/WEBP"
    elif data[:3] == b'GIF':
        fmt = "GIF"
    elif data[:2] == b'PK':
        fmt = "ZIP/container"

    print(f"File: {name}")
    print(f"  Size: {size/1024:.1f} KB, Format: {fmt}")
    print(f"  Magic: {magic}")
    print(f"  First 64 bytes as text: {repr(data[:64])}")
    print()

conn.close()
