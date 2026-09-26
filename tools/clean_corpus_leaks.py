import sys
sys.path.insert(0, r'C:\VedaBaseModern2')
sys.stdout.reconfigure(encoding='utf-8')
import sqlite3
import re

db_path = r'C:\VedaBaseModern2\Database\prabhupada_corpus.db'
conn = sqlite3.connect(db_path)
cur = conn.cursor()

print("--- Running Corpus-Wide Folio Leak Cleaner ---")

# 1. Clean ANTYA chapter ends
cur.execute("SELECT RecordKey, Translation FROM Records WHERE BookKey = 'ANTYA' AND Translation LIKE '%Thus end the Bhaktivedanta purports%'")
rows = cur.fetchall()
antya_fixed = 0
for rkey, trans in rows:
    # Match through the end of "... describing ..." sentence
    m = re.search(r'(Thus end the Bhaktivedanta purports to Śrī Caitanya-caritāmṛta[^\.]*Chapter[^\.]*\.)\s*(Antya\s+\d+.*)$', trans, re.DOTALL)
    if m:
        cleaned_trans = trans[:m.start(2)].strip()
        cur.execute("UPDATE Records SET Translation = ? WHERE RecordKey = ?", (cleaned_trans, rkey))
        cur.execute("UPDATE SearchIndex SET Translation = ? WHERE RecordKey = ?", (cleaned_trans, rkey))
        antya_fixed += 1
print(f"Cleaned {antya_fixed} ANTYA chapter end verses.")

# 2. Clean bookmark tags across entire corpus
LEAK_PATTERNS = [
    r'\\\*ATSUM\s+\d+[\.:]\d+-?\\?\*?',
    r'\\\*LastPlace\\\*',
    r'\\\*last\\\*',
    r'\\\*SPL\s+\d+:?\s*\\\*',
    r'\\\*Sri Isopanisad\\\*',
    r'\\\*Madhya\s+\d+:?[^\*]*\\\*',
    r'\\\*NOD[^\*]*\\\*',
    r'\\\*Narada-bhakti-sutra\\\*',
    r'\\\*Antya\s+\d+:?[^\*]*\\\*',
    r'\\\*SB\s+\d+[\.:]\d+:?[^\*]*\\\*',
    r'\\\*On the Way to Krsna\\\*',
    r'\\\*Raja-vidya\\\*',
    r'\\\*tad-',
    r'\\\*MN\\\*',
    r'\\\*Sri Krsna Caitanya Prabhu[^\*]*\\\*',
    r'\\\*Na\s+Sri Vyasa-[^\*]*\\\*',
]

cur.execute("SELECT RecordKey, BookKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%\\*%' OR Purports LIKE '%\\*%'")
all_leaks = cur.fetchall()
records_fixed = 0

for rkey, bkey, ref, trans, purp in all_leaks:
    new_trans = trans
    new_purp = purp
    
    if new_trans:
        for pat in LEAK_PATTERNS:
            new_trans = re.sub(pat, '', new_trans)
        new_trans = re.sub(r'\s+', ' ', new_trans).strip()
        
    if new_purp:
        for pat in LEAK_PATTERNS:
            new_purp = re.sub(pat, '', new_purp)
        new_purp = re.sub(r'[ \t]+', ' ', new_purp).strip()
        
    if new_trans != trans or new_purp != purp:
        cur.execute("UPDATE Records SET Translation = ?, Purports = ? WHERE RecordKey = ?", (new_trans, new_purp, rkey))
        cur.execute("UPDATE SearchIndex SET Translation = ?, Purports = ? WHERE RecordKey = ?", (new_trans, new_purp, rkey))
        records_fixed += 1

print(f"Cleaned {records_fixed} records with leaked Folio bookmarks.")

conn.commit()
conn.close()
print("Folio leak cleanup complete and committed successfully!")
