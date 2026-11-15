import { randomUUID } from 'node:crypto';
import { liveConfiguration, liveRuntime } from './livePreflight';
import type { LiveRuntime } from './livePreflight';

const quote = (value: string) => `N'${value.replace(/'/g, "''")}'`;
export class LiveData {
  #config: ReturnType<typeof liveConfiguration> | null = null;
  #ready = false;
  #dirty = false;
  #createdLogins = new Set<string>();
  constructor(private readonly runtime: LiveRuntime = liveRuntime()) {}
  readonly prefix = `m188-${randomUUID().replace(/-/g, '')}`;
  readonly loginA = `${this.prefix}-a`;
  readonly loginB = `${this.prefix}-b`;
  readonly tenants = Array.from({ length: 4 }, () => randomUUID());
  readonly taxes = this.tenants.map(() => randomUUID());
  readonly today = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Europe/Warsaw' }).format(new Date());
  readonly yesterday = this.day(-1);
  readonly old = this.day(-40);
  readonly shared = `${this.prefix}-shared`;
  readonly foreign = `${this.prefix}-foreign`;
  readonly documentXml = this.xml('LIVE-HI');
  day(offset: number) { const date = new Date(`${this.today}T12:00:00Z`); date.setUTCDate(date.getUTCDate() + offset); return date.toISOString().slice(0, 10); }
  xml(marker: string) {
    return `<?xml version="1.0" encoding="UTF-8"?><Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/"><Naglowek><KodFormularza kodSystemowy="FA (3)" wersjaSchemy="1-0E">FA</KodFormularza><WariantFormularza>3</WariantFormularza><DataWytworzeniaFa>${this.today}T10:00:00Z</DataWytworzeniaFa><SystemInfo>Synthetic live test</SystemInfo></Naglowek><Podmiot1><DaneIdentyfikacyjne><NIP>1111111111</NIP><Nazwa>Live Seller</Nazwa></DaneIdentyfikacyjne><Adres><KodKraju>PL</KodKraju><AdresL1>Test 1</AdresL1></Adres></Podmiot1><Podmiot2><DaneIdentyfikacyjne><NIP>2222222222</NIP><Nazwa>Live Buyer</Nazwa></DaneIdentyfikacyjne><Adres><KodKraju>PL</KodKraju><AdresL1>Test 2</AdresL1></Adres><JST>2</JST><GV>2</GV></Podmiot2><Fa><KodWaluty>PLN</KodWaluty><P_1>${this.today}</P_1><P_2>${marker}</P_2><P_13_1>100.00</P_13_1><P_14_1>23.00</P_14_1><P_15>123.00</P_15><Adnotacje><P_16>2</P_16><P_17>2</P_17><P_18>2</P_18><P_18A>2</P_18A><Zwolnienie><P_19N>1</P_19N></Zwolnienie><NoweSrodkiTransportu><P_22N>1</P_22N></NoweSrodkiTransportu><P_23>2</P_23><PMarzy><P_PMarzyN>1</P_PMarzyN></PMarzy></Adnotacje><RodzajFaktury>VAT</RodzajFaktury></Fa></Faktura>`;
  }
  private guardedSql(body: string, write: boolean) {
    if (!this.#config || write && !this.#ready) throw new Error('Successful live preflight is required before writes.');
    const server = this.#config.server;
    const guard = `SET NOCOUNT ON; SET XACT_ABORT ON; SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
      IF DB_NAME()<>N'xKsef' OR CONVERT(int,SERVERPROPERTY('IsLocalDB'))<>1 OR CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')) COLLATE Latin1_General_100_BIN2<>${quote(server)} THROW 51000,'Unexpected test database',1;
      BEGIN TRANSACTION; ${body} COMMIT;`;
    return this.runtime.sql(guard);
  }
  private sql(body: string) { return this.guardedSql(body, true); }
  preflight() {
    if (this.#ready) return;
    this.#config = liveConfiguration(this.runtime.env);
    const { pid, applicationName } = this.runtime.apiProcess(this.#config.origin);
    if (!Number.isSafeInteger(pid) || pid <= 0) throw new Error('Invalid local API process identifier.');
    // Read only: prove that the dedicated API process really connects to this LocalDB database.
    this.guardedSql(`IF NOT EXISTS(SELECT 1 FROM sys.dm_exec_sessions WHERE is_user_process=1 AND host_process_id=${pid} AND program_name=${quote(applicationName)} AND database_id=DB_ID())
      OR EXISTS(SELECT 1 FROM sys.dm_exec_sessions WHERE is_user_process=1 AND host_process_id=${pid} AND database_id>4 AND database_id<>DB_ID()) THROW 51002,'API and fixture database mismatch',1;
      IF EXISTS(SELECT 1 FROM AspNetUsers WHERE UserName IN (${quote(this.loginA)},${quote(this.loginB)}))
      OR EXISTS(SELECT 1 FROM Tenants WHERE Id IN (${this.tenants.map(quote).join(',')}))
      OR EXISTS(SELECT 1 FROM TaxEntities WHERE Id IN (${this.taxes.map(quote).join(',')})) THROW 51003,'Fixture identifiers already exist',1;`, false);
    this.#ready = true;
  }
  recordCreatedUser(login: string) {
    if (!this.#ready || ![this.loginA, this.loginB].includes(login)) throw new Error('Cannot record an unowned test user.');
    this.#createdLogins.add(login); this.#dirty = true;
  }
  seed() {
    if (!this.#ready || this.#createdLogins.size !== 2) throw new Error('Confirm both synthetic registrations before seeding SQL.');
    this.#dirty = true;
    const tenants = this.tenants.map((tenant, i) => `INSERT TaxEntities(Id,Nip,Name) VALUES(${quote(this.taxes[i])},'${9000000000 + Number.parseInt(this.prefix.slice(5, 11), 16) + i}',${quote(this.prefix)});
      INSERT Tenants(Id,Name,IsActive,TaxEntityId) VALUES(${quote(tenant)},${quote(`${this.prefix}-${i < 2 ? 'same' : i}`)},1,${quote(this.taxes[i])});
      INSERT UserTenants(TenantId,UserId,Role) SELECT ${quote(tenant)},Id,'User' FROM AspNetUsers WHERE UserName=${quote(i === 3 ? this.loginB : this.loginA)};`).join('\n');
    const invoice = (tenant: number, name: string, amount: string | null, direction = 1, date = this.today, invoicing = this.yesterday, number = `${this.prefix}-${name}`) => {
      const xml = Buffer.from(name === 'HI' ? this.documentXml : this.xml(`LIVE-${name}`)).toString('hex');
      return `INSERT invoices(TenantId,InvoiceNumber,IssueDate,Direction,NetAmount,VatAmount,GrossAmount,Currency,SellerName,SellerNip,BuyerName,BuyerNip,KsefNumber,ImportedAt,InvoicingDate,XmlBytes)
        VALUES(${quote(this.tenants[tenant])},${quote(`LIVE-${name}`)},${quote(date)},${direction},${amount ?? 'NULL'},${amount ?? 'NULL'},${amount ?? '0.00'},'PLN','Live Seller','1111111111','Live Buyer','2222222222',${quote(number)},SYSUTCDATETIME(),${quote(`${invoicing}T12:00:00+00:00`)},0x${xml});`;
    };
    this.sql(`${tenants}
      ${invoice(0, 'HI', '9999999999999999.99', 1, this.today, this.yesterday, this.shared)}
      ${invoice(0, 'LO', '9999999999999999.98')}${invoice(0, 'NEG', '-9999999999999999.99')}
      ${invoice(0, 'ZERO', '0.00')}${invoice(0, 'SMALL', '123.45')}${invoice(0, 'MISSING', null)}
      ${invoice(0, 'DATE', '11.11', 1, this.old, this.today)}${invoice(0, 'PURCHASE', '246.90', 2)}
      ${invoice(1, 'SECOND', '456.78')}${invoice(3, 'B-SHARED', '777.00', 1, this.today, this.yesterday, this.shared)}
      ${invoice(3, 'B-ONLY', '888.00', 1, this.today, this.yesterday, this.foreign)}`);
  }
  revokeMembership() { this.sql(`DELETE UserTenants WHERE TenantId=${quote(this.tenants[1])} AND UserId IN (SELECT Id FROM AspNetUsers WHERE UserName=${quote(this.loginA)});`); }
  private expire(login: string) {
    // Preserve the database lifetime invariant while simulating elapsed idle time.
    this.sql(`UPDATE BrowserSessions SET CreatedAt=DATEADD(hour,-2,SYSUTCDATETIME()),RenewedAt=DATEADD(hour,-1,SYSUTCDATETIME()),IdleExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()),Version=NEWID() WHERE UserId IN (SELECT Id FROM AspNetUsers WHERE UserName=${quote(login)});`);
  }
  expireA() { this.expire(this.loginA); }
  expireB() { this.expire(this.loginB); }
  cleanup() {
    if (!this.#dirty) return;
    const tenants = this.tenants.map(quote).join(','); const taxes = this.taxes.map(quote).join(',');
    const users = [...this.#createdLogins].map(quote).join(',') || "N''";
    this.sql(`DELETE invoices WHERE TenantId IN (${tenants}); DELETE UserTenants WHERE TenantId IN (${tenants});
      DELETE Tenants WHERE Id IN (${tenants}); DELETE TaxEntities WHERE Id IN (${taxes}) AND NOT EXISTS (SELECT 1 FROM Tenants WHERE TaxEntityId=TaxEntities.Id);
      DELETE AspNetUsers WHERE UserName IN (${users});
      IF EXISTS(SELECT 1 FROM invoices WHERE TenantId IN (${tenants})) OR EXISTS(SELECT 1 FROM Tenants WHERE Id IN (${tenants})) OR EXISTS(SELECT 1 FROM TaxEntities WHERE Id IN (${taxes})) OR EXISTS(SELECT 1 FROM AspNetUsers WHERE UserName IN (${users})) THROW 51001,'Live cleanup incomplete',1;`);
    this.#createdLogins.clear(); this.#dirty = false;
  }
}

