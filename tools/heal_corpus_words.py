import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')

db_path = r'C:\VedaBaseModern2\Database\prabhupada_corpus.db'
conn = sqlite3.connect(db_path)
cur = conn.cursor()

print("=== STARTING CORPUS WORD & FOOTNOTE HEALING ===")

# 1. Clean the specific NOD-4 footnote injection
nod4_row = cur.execute("SELECT Purports, Translation FROM Records WHERE RecordKey = 'NOD-4'").fetchone()
if nod4_row:
    target_field = 'Purports' if nod4_row[0] else 'Translation'
    nod4_text = nod4_row[0] or nod4_row[1]
    
    broken_nod4 = (
        r'The conclusion of Śrī Rūpa Gosvāmī is that the devotees who are attracted by the pastimes '
        r'of the Lord in Gokula, or \\\* Vṛndāvana is the transcendental place where Kṛṣṇ\s*\n?\s*a '
        r'enjoys His eternal pastimes as a boy, and it is considered the topmost sphere in all existence\. '
        r'When this Vṛndāvana is exhibited in the material world the place is called Gokula, '
        r'and in the spiritual world it is called Goloka, or Goloka Vṛndāvana\.Vṛndāvana, are the topmost devotees\.'
    )
    
    healed_nod4 = (
        "The conclusion of Śrī Rūpa Gosvāmī is that the devotees who are attracted by the pastimes of the Lord in Gokula, "
        "or Vṛndāvana,* are the topmost devotees.\n\n"
        "*Vṛndāvana is the transcendental place where Kṛṣṇa enjoys His eternal pastimes as a boy, and it is considered "
        "the topmost sphere in all existence. When this Vṛndāvana is exhibited in the material world the place is called Gokula, "
        "and in the spiritual world it is called Goloka, or Goloka Vṛndāvana."
    )
    
    subbed_nod4 = re.sub(broken_nod4, healed_nod4, nod4_text)
    if subbed_nod4 != nod4_text:
        cur.execute(f"UPDATE Records SET {target_field} = ? WHERE RecordKey = 'NOD-4'", (subbed_nod4,))
        cur.execute(f"UPDATE SearchIndex SET {target_field} = ? WHERE RecordKey = 'NOD-4'", (subbed_nod4,))
        print("Healed NOD-4 inline footnote and paragraph structure!")
    else:
        print("Note: NOD-4 regex did not match exactly, trying flexible match...")
        # Flexible match for NOD-4
        pat_flex = re.compile(r'or\s*\\\*\s*Vṛndāvana is the transcendental place.*?Goloka Vṛndāvana\.Vṛndāvana,\s*are the topmost devotees\.', re.DOTALL)
        m = pat_flex.search(nod4_text)
        if m:
            replacement = (
                "or Vṛndāvana,* are the topmost devotees.\n\n"
                "*Vṛndāvana is the transcendental place where Kṛṣṇa enjoys His eternal pastimes as a boy, "
                "and it is considered the topmost sphere in all existence. When this Vṛndāvana is exhibited in the material world "
                "the place is called Gokula, and in the spiritual world it is called Goloka, or Goloka Vṛndāvana."
            )
            subbed_nod4 = nod4_text[:m.start()] + replacement + nod4_text[m.end():]
            cur.execute(f"UPDATE Records SET {target_field} = ? WHERE RecordKey = 'NOD-4'", (subbed_nod4,))
            cur.execute(f"UPDATE SearchIndex SET {target_field} = ? WHERE RecordKey = 'NOD-4'", (subbed_nod4,))
            print("Healed NOD-4 via flexible match!")

# 2. Heal broken words with existing spaces in the database
space_break_rules = [
    # Krsna variations
    (re.compile(r'\bKṛṣ\s+ṇa\b'), 'Kṛṣṇa'),
    (re.compile(r'\bKṛṣṇ\s+a\b'), 'Kṛṣṇa'),
    (re.compile(r'\bK\s+ṛṣṇa\b'), 'Kṛṣṇa'),
    (re.compile(r'\bKṛ\s+ṣṇa\b'), 'Kṛṣṇa'),
    (re.compile(r'\bkṛṣ\s+ṇa\b'), 'kṛṣṇa'),
    (re.compile(r'\bkṛṣṇ\s+a\b'), 'kṛṣṇa'),
    (re.compile(r'\bk\s+ṛṣṇa\b'), 'kṛṣṇa'),
    (re.compile(r'\bkṛ\s+ṣṇa\b'), 'kṛṣṇa'),
    (re.compile(r'\bKṛṣṇ\s+ā\b'), 'Kṛṣṇā'),
    (re.compile(r'\bKṛṣ\s+ṇasya\b'), 'Kṛṣṇasya'),
    # Vaisnava variations
    (re.compile(r'\bVaiṣṇ\s+avas\b'), 'Vaiṣṇavas'),
    (re.compile(r'\bVaiṣṇ\s+ava\b'), 'Vaiṣṇava'),
    (re.compile(r'\bVaiṣ\s+ṇavas\b'), 'Vaiṣṇavas'),
    (re.compile(r'\bVaiṣ\s+ṇava\b'), 'Vaiṣṇava'),
    (re.compile(r'\bVai\s+ṣṇavas\b'), 'Vaiṣṇavas'),
    (re.compile(r'\bVai\s+ṣṇava\b'), 'Vaiṣṇava'),
    # Brahmana variations
    (re.compile(r'\bbrāhma\s+ṇas\b'), 'brāhmaṇas'),
    (re.compile(r'\bbrāhma\s+ṇa\b'), 'brāhmaṇa'),
    (re.compile(r'\bbrāhmaṇ\s+as\b'), 'brāhmaṇas'),
    (re.compile(r'\bbrāhmaṇ\s+a\b'), 'brāhmaṇa'),
    # Visnu variations
    (re.compile(r'\bViṣ\s+ṇu\b'), 'Viṣṇu'),
    (re.compile(r'\bViṣṇ\s+u\b'), 'Viṣṇu'),
    (re.compile(r'\bVi\s+ṣṇu\b'), 'Viṣṇu'),
    (re.compile(r'\bviṣ\s+ṇu\b'), 'viṣṇu'),
    (re.compile(r'\bviṣṇ\s+u\b'), 'viṣṇu'),
    (re.compile(r'\bvi\s+ṣṇu\b'), 'viṣṇu'),
    (re.compile(r'\bviṣ\s+ṇo\b'), 'viṣṇo'),
    # Vrndavana
    (re.compile(r'\bV\s+ṛndāvana\b'), 'Vṛndāvana'),
    (re.compile(r'\bVṛ\s+ndāvana\b'), 'Vṛndāvana'),
    (re.compile(r'\bVṛn\s+dāvana\b'), 'Vṛndāvana'),
    # Other common Sanskrit words
    (re.compile(r'\bPurā\s+ṇas\b'), 'Purāṇas'),
    (re.compile(r'\bPurā\s+ṇa\b'), 'Purāṇa'),
    (re.compile(r'\bRādhārā\s+ṇī\b'), 'Rādhārāṇī'),
    (re.compile(r'\bRādhārāṇ\s+ī\b'), 'Rādhārāṇī'),
    (re.compile(r'\bYudhi\s+ṣṭhira\b'), 'Yudhiṣṭhira'),
    (re.compile(r'\bYudhiṣ\s+ṭhira\b'), 'Yudhiṣṭhira'),
    (re.compile(r'\bVaikuṇ\s+ṭha\b'), 'Vaikuṇṭha'),
    (re.compile(r'\bVaiku\s+ṇṭha\b'), 'Vaikuṇṭha'),
    (re.compile(r'\bPāṇ\s+ḍavas\b'), 'Pāṇḍavas'),
    (re.compile(r'\bPā\s+ṇḍavas\b'), 'Pāṇḍavas'),
    (re.compile(r'\bPaṇ\s+ḍita\b'), 'Paṇḍita'),
    (re.compile(r'\bpaṇ\s+ḍita\b'), 'paṇḍita'),
    (re.compile(r'\bpa\s+ṇḍita\b'), 'paṇḍita'),
    (re.compile(r'\bDak\s+ṣa\b'), 'Dakṣa'),
    (re.compile(r'\bHira\s+ṇyakaśipu\b'), 'Hiraṇyakaśipu'),
    (re.compile(r'\bUpani\s+ṣads\b'), 'Upaniṣads'),
    (re.compile(r'\bUpani\s+ṣad\b'), 'Upaniṣad'),
    (re.compile(r'\bsm\s+ṛtis\b'), 'smṛtis'),
    (re.compile(r'\bsm\s+ṛti\b'), 'smṛti'),
    (re.compile(r'\bAm\s+ṛta\b'), 'Amṛta'),
    (re.compile(r'\bam\s+ṛta\b'), 'amṛta'),
    (re.compile(r'\bcaritāmṛ\s+ta\b'), 'caritāmṛta'),
    (re.compile(r'\bcaritām\s+ṛta\b'), 'caritāmṛta'),
    (re.compile(r'\bNṛsi\s+ṁhadeva\b'), 'Nṛsiṁhadeva'),
    (re.compile(r'\bNṛsi\s+ṁha\b'), 'Nṛsiṁha'),
    (re.compile(r'\bN\s+ṛsi\b'), 'Nṛsi'),
    (re.compile(r'\bP\s+ṛthu\b'), 'Pṛthu'),
    (re.compile(r'\bdṛ\s+ḍha\b'), 'dṛḍha'),
    (re.compile(r'\baṣṭā\s+ṅga\b'), 'aṣṭāṅga'),
    (re.compile(r'\bkṣa\s+triyas\b'), 'kṣatriyas'),
    (re.compile(r'\bkṣa\s+triya\b'), 'kṣatriya'),
    (re.compile(r'\bk\s+ṣatriyas\b'), 'kṣatriyas'),
    (re.compile(r'\bk\s+ṣatriya\b'), 'kṣatriya'),
    (re.compile(r'\bBhaṭṭ\s+ācārya\b'), 'Bhaṭṭācārya'),
    (re.compile(r'\bGuṇḍ\s+icā\b'), 'Guṇḍicā'),
    (re.compile(r'\bSaṅkarṣa\s+ṇa\b'), 'Saṅkarṣaṇa'),
    (re.compile(r'\bSa\s+ṅkarṣaṇa\b'), 'Saṅkarṣaṇa'),
    (re.compile(r'\bNārāya\s+ṇa\b'), 'Nārāyaṇa'),
    (re.compile(r'\bparāya\s+ṇa\b'), 'parāyaṇa'),
    (re.compile(r'\bṬhā\s+kura\b'), 'Ṭhākura'),
    (re.compile(r'\bṬ\s+hākura\b'), 'Ṭhākura'),
    (re.compile(r'\bParīk\s+ṣit\b'), 'Parīkṣit'),
    (re.compile(r'\brasām\s+ṛta\b'), 'rasāmṛta'),
    (re.compile(r'\bpuru\s+ṣa\b'), 'puruṣa'),
    (re.compile(r'\bv\s+ṛtti\b'), 'vṛtti'),
    (re.compile(r'\bCā\s+ṇakya\b'), 'Cāṇakya'),
    (re.compile(r'\bA\s+ṅgirā\b'), 'Aṅgirā'),
    (re.compile(r'\bA\s+ṅga\b'), 'Aṅga'),
    (re.compile(r'\ba\s+ṣṭāṅga\b'), 'aṣṭāṅga'),
    (re.compile(r'\ba\s+ṣṭaka\b'), 'aṣṭaka'),
    (re.compile(r'\bA\s+ṇor\b'), 'Aṇor'),
    (re.compile(r'\ba\s+ṇimā\b'), 'aṇimā'),
]

# 3. Newline split healing across all records
COMMON_WORDS = set("""
a about above after again against all also am an and another any are as at 
be because been before being between both but by 
came can cannot could 
day did do does down during 
each even every 
first for from 
get give go good great 
had has have he her here him his how 
i if in into is it its 
just 
know 
last like little long lord 
made make man many may me men might more most much must my 
never new no not now 
of off old on one only or other our out over own 
people 
right 
said same see she should since so some state still such 
take than that the their them then there these they this those three through time to too two 
under up upon us used 
very 
was way we well were what when where which while who will with without world would 
year years you your
""".split())

DIACRITIC_FRAGMENTS = {
    'ṇa', 'ṇas', 'ṣṇa', 'ṣṇas', 'ṣa', 'ṣas', 'ṭha', 'ṭhas', 'ḍita', 'ḍitas', 
    'ṇī', 'ṇīs', 'ācārya', 'ācāryas', 'ndāvana', 'ṣṭhira', 'ṣit', 'ṅkarṣaṇa',
    'ṣatriya', 'ṣatriyas', 'hākura', 'ṇḍavas', 'ṇyakaśipu', 'ṛṣis', 'ṛta', 'ṛtas',
    'ṣad', 'ṣads', 'ḍha', 'stha', 'sthas', 'bhūta', 'maya', 'mayī', 'devī', 'icā',
    'ṅga', 'ṅgirā', 'ṣṭāṅga', 'ṣṭaka', 'ṇor', 'ṇimā'
}

cur.execute("SELECT RecordKey, Translation, Purports FROM Records")
records = cur.fetchall()

total_words_healed = 0
records_updated = 0

newline_pat = re.compile(r'([a-zA-ZāīūṛṝḷḹñṅṇśṣṭḍĀĪŪṚṜḶḸÑṄṆŚṢṬḌ]+)(-?)\r?\n([a-zA-Zāīūṛṝḷḹñṅṇśṣṭḍ]+)')

def heal_text(text):
    if not text:
        return text, 0
    healed_count = 0
    
    # First: heal newline splits
    def repl(m):
        nonlocal healed_count
        w1 = m.group(1)
        hyp = m.group(2)
        w2 = m.group(3)
        w1_l = w1.lower()
        w2_l = w2.lower()
        
        # If hyphenated at line break: join without hyphen
        if hyp == '-':
            healed_count += 1
            return w1 + w2
            
        # Single vowel suffix completing diacritic stem (Kṛṣṇ + a, brāhmaṇ + a)
        if len(w2) == 1 and w2 in 'aāiīuūeoṛ':
            healed_count += 1
            return w1 + w2
            
        # If w2 is a known diacritic fragment (ṇa, ṣṇa, ḍita, etc.)
        if w2_l in DIACRITIC_FRAGMENTS:
            healed_count += 1
            return w1 + w2
            
        # If w1 is a common word and w2 is not a fragment, keep space
        if w1_l in COMMON_WORDS:
            return f"{w1} {w2}"
            
        # Otherwise it's a split word fragment (Kṛ + ṣṇa, Vaiṣ + ṇava, etc.)
        healed_count += 1
        return w1 + w2

    new_t = newline_pat.sub(repl, text)
    
    # Second: heal space break rules
    for pat, rep in space_break_rules:
        if pat.search(new_t):
            sub_t = pat.sub(rep, new_t)
            if sub_t != new_t:
                healed_count += 1
                new_t = sub_t
                
    return new_t, healed_count

for rkey, trans, purp in records:
    new_trans, c1 = heal_text(trans)
    new_purp, c2 = heal_text(purp)
    
    if new_trans != trans or new_purp != purp:
        cur.execute("UPDATE Records SET Translation = ?, Purports = ? WHERE RecordKey = ?", (new_trans, new_purp, rkey))
        cur.execute("UPDATE SearchIndex SET Translation = ?, Purports = ? WHERE RecordKey = ?", (new_trans, new_purp, rkey))
        total_words_healed += (c1 + c2)
        records_updated += 1

conn.commit()
conn.close()

print("\n=== HEALING COMPLETED SUCCESSFULLY ===")
print(f"Total split words healed: {total_words_healed}")
print(f"Total records updated: {records_updated}")
