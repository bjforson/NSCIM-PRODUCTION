import psycopg2, os, requests

pw = os.environ.get('NICKSCAN_DB_PASSWORD', '')
conn = psycopg2.connect(host='localhost', dbname='nickscan_production', user='postgres', password=pw)
cur = conn.cursor()

# Get first cross-record scan with image
cur.execute("""
    SELECT crs.container1, crs.container2, crs.scannertype, crs.scannerrecordid::text,
           a.scanimage
    FROM crossrecordscans crs
    JOIN asescans a ON a.id = crs.scannerrecordid
    WHERE a.scanimage IS NOT NULL
    LIMIT 1
""")
row = cur.fetchone()
c1, c2, scanner, rec_id, img = row
image_data = bytes(img)
print(f"Test: {c1}|{c2} scanner={scanner} image={len(image_data)} bytes")

resp = requests.post(
    "http://localhost:5310/api/split/upload",
    files={"file": ("scan.jpg", image_data, "image/jpeg")},
    data={"container_numbers": f"{c1},{c2}", "scanner_type": scanner},
    timeout=30
)
print(f"Status: {resp.status_code}")
print(f"Response: {resp.text[:500]}")
conn.close()
