function embedPDF(bytes, elementID, uuid)
{
    const file = new File([bytes], uuid, { type: 'application/pdf' });
    const objectURL = URL.createObjectURL(file);

    PDFObject.embed(objectURL, elementID, { page: 1, view: 'FitW' });

    URL.revokeObjectURL(objectURL);
}
