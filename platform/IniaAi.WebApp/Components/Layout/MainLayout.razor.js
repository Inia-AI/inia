downloadFileFromStream = async (fileName, streamRef) => {
    const buffer = await streamRef.arrayBuffer();
    const blob = new Blob([buffer]);
    const url = window.URL.createObjectURL(blob);

    const downloadLink = document.createElement("a");
    downloadLink.href = url;
    downloadLink.download = fileName;

    document.body.appendChild(downloadLink);
    downloadLink.click();
    document.body.removeChild(downloadLink);
    window.URL.revokeObjectURL(url);
};

downloadFile = (fileName, data) => {
    const blob = new Blob([data], { type: "text/plain" });
    const url = window.URL.createObjectURL(blob);

    const downloadLink = document.createElement("a");
    downloadLink.href = url;
    downloadLink.download = fileName;

    document.body.appendChild(downloadLink);
    downloadLink.click();
    document.body.removeChild(downloadLink);
    window.URL.revokeObjectURL(url);
};
