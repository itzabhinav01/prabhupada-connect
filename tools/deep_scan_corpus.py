import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect('Database/prabhupada_corpus.db')
cur = conn.cursor()

print("=== DEEP CORPUS SANITY AUDIT ===")

# 1. Audit 'Thus end' tails
cur.execute("SELECT RecordKey, BookKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%Thus end%' OR Purports LIKE '%Thus end%'")
rows = cur.fetchall()

thus_end_leaks = []
for rkey, bkey, ref, trans, purp in rows:
    for field_name, text in [('Translation', trans), ('Purports', purp)]:
        if not text:
            continue
        m = re.search(r'Thus end the Bhaktivedanta purports to.*?\.', text, re.IGNORECASE)
        if m:
            tail = text[m.end():].strip()
            # If tail starts with quotes or spaces, trim
            tail_clean = tail.lstrip(' "\'')
            if tail_clean:
                # Is it an actual leak? Check if it has chapter markers or book references
                if any(k in tail_clean for k in ['Chapter', '—', ':', 'Canto', 'Ādi', 'Adi', 'Madhya', 'Antya', 'SB ']):
                    thus_end_leaks.append((rkey, bkey, ref, field_name, m.end(), tail))

print(f"1. Leaks after 'Thus end the Bhaktivedanta purports': {len(thus_end_leaks)}")

# 2. Audit remaining raw Folio escape sequences or bookmarks
# (e.g. \* or ^ or weird control chars like \x00-\x08, \x0b-\x1f)
cur.execute("SELECT RecordKey, BookKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%\\*%' OR Purports LIKE '%\\*%'")
asterisk_rows = cur.fetchall()
print(f"2. Records with raw Folio bookmarks/escapes ('\\*'): {len(asterisk_rows)}")
for rkey, bkey, ref, trans, purp in asterisk_rows[:10]:
    sample = (trans or "") + " " + (purp or "")
    m = re.findall(r'\\\*[^\s\*]+(?:\s+[^\*]+)?\\\*?', sample)
    print(f"   [{rkey}] ({ref}): {m[:3]}")

# 3. Audit broken split words (like 'afflic ted', 'caritāmṛ ta', 'Am ṛta', 'Bhaktive danta', etc.)
# Patterns like: lowercase letter, space, 2-3 lowercase letters where joining is standard
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%caritāmṛ ta%' OR Purports LIKE '%caritāmṛ ta%' OR Translation LIKE '%Am ṛta%' OR Purports LIKE '%Am ṛta%'")
broken_rows = cur.fetchall()
print(f"3. Records with broken 'caritāmṛ ta' or 'Am ṛta': {len(broken_rows)}")

# 4. Audit Songs and Mantras: any remaining purports where there shouldn't be, or missing verse structure
cur.execute("SELECT RecordKey, Reference, Purports FROM Records WHERE BookKey IN ('SVA', 'TMG')")
song_rows = cur.fetchall()
songs_with_purport_key = 0
for rkey, ref, purp in song_rows:
    if purp and '"purport"' in purp:
        songs_with_purport_key += 1
print(f"4. SVA/TMG songs still containing 'purport' key in JSON: {songs_with_purport_key}")

conn.close()
