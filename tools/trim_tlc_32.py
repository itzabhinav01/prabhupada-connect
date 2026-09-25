import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
cur = conn.cursor()

cur.execute("SELECT rowid, Purports FROM Records WHERE RecordKey='TLC-32'")
rowid, text = cur.fetchone()

idx = text.find("l0TheNectarofDevotion")
if idx != -1:
    clean_text = text[:idx].strip()
    cur.execute("UPDATE Records SET Purports=? WHERE RecordKey='TLC-32'", (clean_text,))
    cur.execute("DELETE FROM RecordsFts WHERE rowid=?", (rowid,))
    cur.execute("""
        INSERT INTO RecordsFts (rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
        SELECT rowid, RecordKey, Reference, Title, Devanagari, Transliteration, Synonyms, Translation, Purports
        FROM Records WHERE rowid=?
    """, (rowid,))
    conn.commit()
    print(f"SUCCESS: TLC-32 trimmed from {len(text)} to {len(clean_text)} bytes!")
else:
    print("l0TheNectarofDevotion not found")
