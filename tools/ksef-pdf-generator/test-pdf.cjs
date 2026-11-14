globalThis.FileReader = class FileReader {
    result = null;
    error = null;
    onload = null;
    onerror = null;

    async readAsText(file) {
        try {
            this.result = await file.text();
            this.onload?.({ target: this });
        } catch (error) {
            this.error = error;
            this.onerror?.({ target: this });
        }
    }

    async readAsArrayBuffer(file) {
        try {
            this.result = await file.arrayBuffer();
            this.onload?.({ target: this });
        } catch (error) {
            this.error = error;
            this.onerror?.({ target: this });
        }
    }

    async readAsDataURL(file) {
        try {
            const buffer = Buffer.from(await file.arrayBuffer());
            const mimeType = file.type || 'application/octet-stream';

            this.result = `data:${mimeType};base64,${buffer.toString('base64')}`;
            this.onload?.({ target: this });
        } catch (error) {
            this.error = error;
            this.onerror?.({ target: this });
        }
    }
};

const fs = require('node:fs');
const { generateInvoice } = require('./dist/ksef-fe-invoice-converter.umd.cjs');

async function main() {
    const xml = fs.readFileSync('./test-invoice.xml', 'utf8');

    const file = new File(
        [xml],
        'invoice.xml',
        { type: 'application/xml' }
    );

    const additionalData = {
        nrKSeF: '7841476673-20261018-7BBBE1C00000-7D'
    };

    const base64 = await generateInvoice(
        file,
        additionalData,
        'base64'
    );

    fs.writeFileSync(
        './test-invoice.pdf',
        Buffer.from(base64, 'base64')
    );

    console.log('PDF wygenerowany');
}

main().catch(console.error);