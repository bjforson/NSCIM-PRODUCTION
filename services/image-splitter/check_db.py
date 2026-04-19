import psycopg2, os

pw = os.environ.get('NICKSCAN_DB_PASSWORD', '')
conn = psycopg2.connect(host='localhost', dbname='nickscan_production', user='postgres', password=pw)
cur = conn.cursor()

print(f"Cross-record scans: ", end="")
cur.execute("SELECT COUNT(*) FROM crossrecordscans")
print(cur.fetchone()[0])

# Check image availability - all ASE based on sample above
cur.execute("""
    SELECT
        crs.container1, crs.container2, crs.scannertype,
        CASE WHEN a.scanimage IS NOT NULL THEN 'YES' ELSE 'NO' END as has_img,
        octet_length(a.scanimage) as img_bytes
    FROM crossrecordscans crs
    LEFT JOIN asescans a ON a.id = crs.scannerrecordid
    ORDER BY crs.createdat DESC
    LIMIT 10
""")
rows = cur.fetchall()
print("\nImage availability (ASE):")
for r in rows:
    size = f"{r[4]/1024:.0f}KB" if r[4] else "none"
    print(f"  {r[0]} | {r[1]} | {r[2]} | img={r[3]} ({size})")

# Image split jobs
cur.execute("SELECT COUNT(*), status FROM image_split_jobs GROUP BY status")
rows = cur.fetchall()
print(f"\nImage split jobs by status:")
for r in rows:
    print(f"  {r[1]}: {r[0]}")

cur.execute("""
    SELECT container_numbers, status, best_strategy, ROUND(CAST(best_score AS NUMERIC),3)
    FROM image_split_jobs ORDER BY created_at DESC LIMIT 5
""")
rows = cur.fetchall()
print("\nRecent split jobs:")
for r in rows:
    print(f"  {r[0]} | {r[1]} | strategy={r[2]} | score={r[3]}")

conn.close()
