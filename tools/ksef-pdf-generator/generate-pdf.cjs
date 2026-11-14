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
        } catch (error) {async function main() {
    const [inputPath, outputPath, ksefNumber] = process.argv.slice(2);

    if (!inputPath || !outputPath || !ksefNumber) {
        throw new Error(
            'Użycie: node generate-pdf.cjs input.xml output.pdf numerKsef'
        );
    }

    const xml = fs.readFileSync(inputPath, 'utf8');

    const file = new File(
        [xml],
        'invoice.xml',
        { type: 'application/xml' }
    );

    const additionalData = {
        nrKSeF: ksefNumber
    };

    const base64 = await generateInvoice(
        file,
        additionalData,
        'base64'
    );

    fs.writeFileSync(
        outputPath,
        Buffer.from(base64, 'base64')
    );

    console.log('PDF wygenerowany');
}
            this.error = error;
            this.onerror?.({ target: this });
        }
    }
};

const fs = require('node:fs');
const { generateInvoice } = require('./dist/ksef-fe-invoice-converter.umd.cjs');

async function main() {
    const [inputPath, outputPath, ksefNumber] = process.argv.slice(2);

    if (!inputPath || !outputPath || !ksefNumber) {
        throw new Error(
            'Użycie: node generate-pdf.cjs input.xml output.pdf numerKsef'
        );
    }

    const xml = fs.readFileSync(inputPath, 'utf8');

    const file = new File(
        [xml],
        'invoice.xml',
        { type: 'application/xml' }
    );

    const additionalData = {
        nrKSeF: ksefNumber
    };

    const base64 = await generateInvoice(
        file,
        additionalData,
        'base64'
    );

    fs.writeFileSync(
        outputPath,
        Buffer.from(base64, 'base64')
    );

    console.log('PDF wygenerowany');
}

main().catch(error => {
    console.error('Błąd generowania PDF:', error);
    process.exitCode = 1;
});